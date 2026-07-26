using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by MinerUAgentFactory")]
internal sealed class MinerUAgent : DelegatingAIAgent
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger _logger;

    public MinerUAgent(
        AIAgent innerAgent,
        IHttpContextAccessor httpContextAccessor,
        ILogger logger)
        : base(innerAgent)
    {
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
        var enriched = PrepareMessages(messages.ToList());

        await foreach (var update in InnerAgent.RunStreamingAsync(enriched, thread, options, cancellationToken).ConfigureAwait(false))
            yield return update;

        // If fill_form was called by the LLM, emit the state as DataContent so the frontend picks it up
        if (_httpContextAccessor.HttpContext?.Items["__form_fill__"] is MinerUFormFill formFill)
        {
            var stateBytes = JsonSerializer.SerializeToUtf8Bytes(formFill);
            yield return new AgentRunResponseUpdate
            {
                Contents = [new DataContent(stateBytes, "application/json")]
            };
            _logger.LogInformation("Emitted form fill state: formId={FormId}", formFill.FormId);
            _httpContextAccessor.HttpContext.Items.Remove("__form_fill__");
        }
    }

    // ── Message preparation ────────────────────────────────────────────────────

    private IEnumerable<ChatMessage> PrepareMessages(List<ChatMessage> messages)
    {
        // Move any DataContent files from the last user message into HttpContext.Items
        // so the parse_documents tool can access them.
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var dataContentFiles = lastUserMessage?.Contents
            .OfType<DataContent>()
            .Where(d => IsDocumentType(d.MediaType))
            .Select(d => new ExtractedFile(ExtractBytes(d), d.MediaType ?? "application/octet-stream"))
            .Where(f => f.Bytes.Length > 0)
            .ToList() ?? [];

        if (dataContentFiles.Count > 0 && _httpContextAccessor.HttpContext is { } ctx)
        {
            var existing = ctx.Items["__attachments__"] as List<ExtractedFile> ?? [];
            ctx.Items["__attachments__"] = existing.Concat(dataContentFiles).ToList();
            _logger.LogInformation("Stored {Count} DataContent file(s) for parse_documents tool", dataContentFiles.Count);
        }

        var totalFiles = ((_httpContextAccessor.HttpContext?.Items["__attachments__"] as List<ExtractedFile>) ?? []).Count;
        if (totalFiles == 0)
            return messages;

        _logger.LogInformation("{Count} file(s) ready for tool processing", totalFiles);

        var hint = new ChatMessage(
            ChatRole.System,
            $"The user has uploaded {totalFiles} file(s). Call the parse_documents tool to extract their content, " +
            "then call get_forms to retrieve available forms and fill in the matching form fields.");

        return messages.Prepend(hint);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

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

// ── State & DTO models ─────────────────────────────────────────────────────────

internal sealed class MinerUFormFill
{
    [JsonPropertyName("formId")]
    public string? FormId { get; set; }

    [JsonPropertyName("formTitle")]
    public string? FormTitle { get; set; }

    [JsonPropertyName("filledValues")]
    public Dictionary<string, JsonElement>? FilledValues { get; set; }
}

internal sealed class FormDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("fields")]
    public List<FormFieldDto>? Fields { get; set; }
}

internal sealed class FormFieldDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("options")]
    public List<FieldOptionDto>? Options { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

internal sealed class FieldOptionDto
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}
