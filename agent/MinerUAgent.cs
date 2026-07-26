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
    private readonly MinerUCloudService _minerUService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger _logger;
    private readonly IChatClient _chatClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _nextjsBaseUrl;

    public MinerUAgent(
        AIAgent innerAgent,
        MinerUCloudService minerUService,
        IHttpContextAccessor httpContextAccessor,
        ILogger logger,
        IChatClient chatClient,
        IHttpClientFactory httpClientFactory,
        string nextjsBaseUrl)
        : base(innerAgent)
    {
        _minerUService = minerUService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _chatClient = chatClient;
        _httpClientFactory = httpClientFactory;
        _nextjsBaseUrl = nextjsBaseUrl;
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
        var (enriched, extractedContent) = await EnrichWithMinerUAsync(messages.ToList(), cancellationToken);

        if (extractedContent is not null)
        {
            var formFill = await TryFillFormAsync(extractedContent, cancellationToken);
            if (formFill is not null)
            {
                var stateBytes = JsonSerializer.SerializeToUtf8Bytes(formFill);
                yield return new AgentRunResponseUpdate
                {
                    Contents = [new DataContent(stateBytes, "application/json")]
                };
                _logger.LogInformation("Emitted form fill state: formId={FormId}", formFill.FormId);
            }
        }

        await foreach (var update in InnerAgent.RunStreamingAsync(enriched, thread, options, cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    // ── Document extraction ────────────────────────────────────────────────────

    private async Task<(IEnumerable<ChatMessage> Messages, string? ExtractedContent)> EnrichWithMinerUAsync(
        List<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var middlewareFiles = _httpContextAccessor.HttpContext?.Items["__attachments__"] as List<ExtractedFile> ?? [];

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

        if (allFiles.Count == 0) return (messages, null);

        var parsed = new List<string>();
        foreach (var file in allFiles)
        {
            _logger.LogInformation("Sending file to MinerU: {MediaType}", file.MediaType);
            var result = await ParseWithMinerUAsync(file.Bytes, file.MediaType, cancellationToken);
            if (result is not null)
                parsed.Add(result);
        }

        if (parsed.Count == 0) return (messages, null);

        var extractedContent = string.Join("\n\n---\n\n", parsed);

        var systemMessage = new ChatMessage(
            ChatRole.System,
            $"""
            The user has uploaded file(s). MinerU has extracted the following content:

            {extractedContent}

            Use this extracted content to answer the user's request.
            """);

        return (messages.Prepend(systemMessage), extractedContent);
    }

    // ── Form matching & filling ────────────────────────────────────────────────

    private async Task<MinerUFormFill?> TryFillFormAsync(string extractedContent, CancellationToken ct)
    {
        var forms = await FetchFormsAsync(ct);
        if (forms.Count == 0)
        {
            _logger.LogInformation("No forms available, skipping form match");
            return null;
        }

        _logger.LogInformation("Matching document against {Count} form(s)", forms.Count);

        var formsJson = JsonSerializer.Serialize(forms);
        var snippet = extractedContent.Length > 4000
            ? extractedContent[..4000] + "\n... [truncated]"
            : extractedContent;

        var prompt = $$"""
            You are a form-filling assistant. Given a document's extracted text and a list of available forms, your job is to:
            1. Pick the form whose purpose best matches the document.
            2. Extract the relevant value for each field from the document text.

            Available forms (JSON):
            {{formsJson}}

            Extracted document content:
            {{snippet}}

            Respond ONLY with valid JSON — no markdown, no extra text:
            {
              "formId": "<id of the best matching form, or null if nothing fits>",
              "formTitle": "<title of the matched form>",
              "filledValues": {
                "<fieldId>": "<extracted value>"
              }
            }

            Rules:
            - If no form is a reasonable match, set formId to null and filledValues to {}.
            - For select/radio fields, use one of the available option values exactly.
            - For checkbox fields, use an array of matching option values.
            - For date fields, use ISO format YYYY-MM-DD.
            - Use empty string "" for fields where no value is found.
            """;

        try
        {
            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                ct);

            _logger.LogInformation("Form fill LLM response: {Json}", response.Text);

            var result = JsonSerializer.Deserialize<MinerUFormFill>(response.Text);
            return result?.FormId is null ? null : result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Form fill LLM call failed");
            return null;
        }
    }

    private async Task<List<FormDto>> FetchFormsAsync(CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            var json = await client.GetStringAsync($"{_nextjsBaseUrl}/api/forms", ct);
            return JsonSerializer.Deserialize<List<FormDto>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch forms from {Url}", _nextjsBaseUrl);
            return [];
        }
    }

    // ── MinerU parsing ─────────────────────────────────────────────────────────

    private async Task<string?> ParseWithMinerUAsync(byte[] bytes, string mediaType, CancellationToken ct)
    {
        try
        {
            var fileName = GuessFileName(mediaType);
            return await _minerUService.ParseAsync(bytes, fileName, mediaType, ct);
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
