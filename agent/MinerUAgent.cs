using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by MinerUAgentFactory")]
internal sealed class MinerUAgent : DelegatingAIAgent
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _minerUBaseUrl;
    private readonly ILogger _logger;

    public MinerUAgent(AIAgent innerAgent, IHttpClientFactory httpClientFactory, string minerUBaseUrl, ILogger logger)
        : base(innerAgent)
    {
        _httpClientFactory = httpClientFactory;
        _minerUBaseUrl = minerUBaseUrl;
        _logger = logger;
    }

    public override Task<AgentRunResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return RunStreamingAsync(messages, thread, options, cancellationToken).ToAgentRunResponseAsync(cancellationToken);
    }

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enriched = await EnrichWithMinerUAsync(messages.ToList(), cancellationToken);

        await foreach (var update in InnerAgent.RunStreamingAsync(enriched, thread, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private async Task<IEnumerable<ChatMessage>> EnrichWithMinerUAsync(List<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUserMessage is null) return messages;

        var files = lastUserMessage.Contents
            .OfType<DataContent>()
            .Where(d => IsDocumentType(d.MediaType))
            .ToList();

        if (files.Count == 0) return messages;

        var parsed = new List<string>();
        foreach (var file in files)
        {
            _logger.LogInformation("📄 Sending file to MinerU: {MediaType}", file.MediaType);
            var result = await ParseWithMinerUAsync(file, cancellationToken);
            if (result is not null)
                parsed.Add(result);
        }

        if (parsed.Count == 0) return messages;

        var extractedContent = string.Join("\n\n---\n\n", parsed);
        var systemMessage = new ChatMessage(
            ChatRole.System,
            $"""
            The user has uploaded file(s). MinerU has extracted the following content:

            {extractedContent}

            Use this extracted content to answer the user's request.
            """);

        return messages.Prepend(systemMessage);
    }

    private async Task<string?> ParseWithMinerUAsync(DataContent fileContent, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = ExtractBytes(fileContent);
            if (bytes.Length == 0) return null;

            using var client = _httpClientFactory.CreateClient("MinerU");
            using var form = new MultipartFormDataContent();
            var fileBytes = new ByteArrayContent(bytes);
            fileBytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                fileContent.MediaType ?? "application/octet-stream");
            form.Add(fileBytes, "file", "upload");

            var response = await client.PostAsync($"{_minerUBaseUrl}/api/v1/extract", form, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<MinerUResponse>(json);
            return result?.Markdown ?? result?.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MinerU parsing failed");
            return $"[MinerU parsing failed: {ex.Message}]";
        }
    }

    private static byte[] ExtractBytes(DataContent content)
    {
        if (!content.Data.IsEmpty)
            return content.Data.ToArray();

        if (content.Uri is { } uri && uri.StartsWith("data:"))
        {
            var commaIndex = uri.IndexOf(',');
            if (commaIndex >= 0)
                return Convert.FromBase64String(uri[(commaIndex + 1)..]);
        }

        return [];
    }

    private static bool IsDocumentType(string? mediaType) =>
        mediaType is not null &&
        (mediaType.Contains("pdf") ||
         mediaType.Contains("word") ||
         mediaType.Contains("document") ||
         mediaType.Contains("image") ||
         mediaType.Contains("text/plain"));
}

internal sealed class MinerUResponse
{
    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
