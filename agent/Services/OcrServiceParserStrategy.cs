using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class OcrServiceParserStrategy : IDocumentParserStrategy
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly string _ocrUrl;

    public OcrServiceParserStrategy(IHttpClientFactory httpClientFactory, ILogger logger, string ocrUrl)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _ocrUrl = ocrUrl.TrimEnd('/');
    }

    public async Task<string?> ParseAsync(byte[] fileBytes, string fileName, string mediaType, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        content.Add(fileContent, "file", fileName);

        _logger.LogInformation("OCR sending {FileName} ({Size} bytes) to {Url}", fileName, fileBytes.Length, _ocrUrl);

        var response = await client.PostAsync($"{_ocrUrl}/ocr", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<OcrResponse>(json);

        if (result?.Pages is null || result.Pages.Count == 0)
        {
            _logger.LogWarning("OCR returned no pages");
            return null;
        }

        var extracted = new List<string>();
        foreach (var page in result.Pages)
        {
            var lines = page.Lines?.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t));
            if (lines is not null && lines.Any())
                extracted.Add(string.Join("\n", lines));
        }

        var text = string.Join("\n\n---\n\n", extracted);
        _logger.LogInformation("OCR extracted {Length} chars from {FileName}", text.Length, fileName);
        return text;
    }

    private sealed class OcrResponse
    {
        [JsonPropertyName("pages")] public List<OcrPage>? Pages { get; set; }
    }

    private sealed class OcrPage
    {
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("lines")] public List<OcrLine>? Lines { get; set; }
    }

    private sealed class OcrLine
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }
}
