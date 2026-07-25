using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by MinerUAgentFactory")]
internal sealed class MinerUAgent : DelegatingAIAgent
{
    private readonly MinerUCloudService _minerUService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger _logger;

    public MinerUAgent(
        AIAgent innerAgent,
        MinerUCloudService minerUService,
        IHttpContextAccessor httpContextAccessor,
        ILogger logger)
        : base(innerAgent)
    {
        _minerUService = minerUService;
        _httpContextAccessor = httpContextAccessor;
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
        // Files extracted by FileAttachmentMiddleware from array-content messages
        var middlewareFiles = _httpContextAccessor.HttpContext?.Items["__attachments__"] as List<ExtractedFile> ?? [];

        // Also check native DataContent in messages (future-proofing)
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var dataContentFiles = lastUserMessage?.Contents
            .OfType<DataContent>()
            .Where(d => IsDocumentType(d.MediaType))
            .Select(d => new ExtractedFile(ExtractBytes(d), d.MediaType ?? "application/octet-stream"))
            ?? [];

        var allFiles = middlewareFiles
            .Concat(dataContentFiles)
            .Where(f => f.Bytes.Length > 0 && IsDocumentType(f.MediaType))
            .ToList();

        if (allFiles.Count == 0) return messages;

        var parsed = new List<string>();
        foreach (var file in allFiles)
        {
            _logger.LogInformation("📄 Sending file to MinerU: {MediaType}", file.MediaType);
            var result = await ParseWithMinerUAsync(file.Bytes, file.MediaType, cancellationToken);
            if (result is not null)
                parsed.Add(result);
        }

        if (parsed.Count == 0) return messages;

        var systemMessage = new ChatMessage(
            ChatRole.System,
            $"""
            The user has uploaded file(s). MinerU has extracted the following content:

            {string.Join("\n\n---\n\n", parsed)}

            Use this extracted content to answer the user's request.
            """);

        return messages.Prepend(systemMessage);
    }

    private async Task<string?> ParseWithMinerUAsync(byte[] bytes, string mediaType, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = GuessFileName(mediaType);
            return await _minerUService.ParseAsync(bytes, fileName, mediaType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MinerU parsing failed");
            return $"[MinerU parsing failed: {ex.Message}]";
        }
    }

    private static string GuessFileName(string mediaType) => mediaType switch
    {
        "application/pdf" => "document.pdf",
        "image/png" => "image.png",
        "image/jpeg" or "image/jpg" => "image.jpg",
        "image/webp" => "image.webp",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "document.docx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "document.pptx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "document.xlsx",
        _ => "document"
    };

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
         mediaType.Contains("text/plain") ||
         mediaType.Contains("presentationml") ||
         mediaType.Contains("spreadsheetml"));
}
