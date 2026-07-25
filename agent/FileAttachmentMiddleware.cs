using System.Text;
using System.Text.Json.Nodes;

internal sealed class FileAttachmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Method == "POST" &&
            context.Request.ContentType?.Contains("application/json") == true)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (TryTransform(body, out var transformed, out var attachments))
            {
                if (attachments.Count > 0)
                    context.Items["__attachments__"] = attachments;

                var bytes = Encoding.UTF8.GetBytes(transformed);
                context.Request.Body = new MemoryStream(bytes);
                context.Request.ContentLength = bytes.Length;
            }
        }

        await next(context);
    }

    private static bool TryTransform(string json, out string transformed, out List<ExtractedFile> attachments)
    {
        attachments = [];
        transformed = json;

        try
        {
            var root = JsonNode.Parse(json);
            if (root?["messages"] is not JsonArray messages) return false;

            bool changed = false;
            foreach (var msg in messages.OfType<JsonObject>())
            {
                if (msg["content"] is not JsonArray contentArray) continue;

                var textParts = new List<string>();
                foreach (var part in contentArray.OfType<JsonObject>())
                {
                    var type = part["type"]?.GetValue<string>();
                    switch (type)
                    {
                        case "text":
                            textParts.Add(part["text"]?.GetValue<string>() ?? "");
                            break;

                        // AG-UI format: {type:"image"|"document"|"audio"|"video", source:{type:"data", value:base64, mimeType:...}}
                        case "image":
                        case "document":
                        case "audio":
                        case "video":
                            var source = part["source"]?.AsObject();
                            if (source?["type"]?.GetValue<string>() == "data")
                            {
                                var value = source["value"]?.GetValue<string>();
                                var mime = source["mimeType"]?.GetValue<string>() ?? "application/octet-stream";
                                if (value is not null)
                                    attachments.Add(new ExtractedFile(Convert.FromBase64String(value), mime));
                            }
                            break;

                        // AG-UI format: {type:"binary", mimeType:..., data:base64}
                        case "binary":
                            var binData = part["data"]?.GetValue<string>();
                            var binMime = part["mimeType"]?.GetValue<string>() ?? "application/octet-stream";
                            if (binData is not null)
                                attachments.Add(new ExtractedFile(Convert.FromBase64String(binData), binMime));
                            break;

                        // OpenAI format (fallback): {type:"image_url", image_url:{url:"data:..."}}
                        case "image_url":
                            var url = part["image_url"]?["url"]?.GetValue<string>();
                            if (url?.StartsWith("data:") == true)
                                attachments.Add(ParseDataUri(url));
                            break;

                        // OpenAI format (fallback): {type:"file", file:{content:base64, type:mime}}
                        case "file":
                            var fileObj = part["file"]?.AsObject();
                            if (fileObj is not null)
                            {
                                var content = fileObj["content"]?.GetValue<string>();
                                var mediaType = fileObj["type"]?.GetValue<string>() ?? "application/octet-stream";
                                if (content is not null)
                                    attachments.Add(new ExtractedFile(Convert.FromBase64String(content), mediaType));
                            }
                            break;
                    }
                }

                msg["content"] = string.Join("\n", textParts);
                changed = true;
            }

            if (!changed) return false;
            transformed = root!.ToJsonString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ExtractedFile ParseDataUri(string uri)
    {
        // data:<mediatype>;base64,<data>
        var commaIdx = uri.IndexOf(',');
        var meta = uri[5..Math.Max(commaIdx, 5)];
        var semiIdx = meta.IndexOf(';');
        var mediaType = semiIdx >= 0 ? meta[..semiIdx] : meta;
        var base64 = commaIdx >= 0 ? uri[(commaIdx + 1)..] : string.Empty;
        return new ExtractedFile(Convert.FromBase64String(base64), mediaType);
    }
}

internal sealed record ExtractedFile(byte[] Bytes, string MediaType);
