using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ComponentModel;
using System.Text.Json;

public class MinerUAgentFactory : IAgentFactory
{
    public string Route => "/minerU";

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly OpenAIClient _openAiClient;
    private readonly ILogger _logger;
    private readonly string _nextjsBaseUrl;
    private readonly MinerUCloudService _minerUService;

    public MinerUAgentFactory(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        JsonSerializerOptions jsonSerializerOptions)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _jsonSerializerOptions = jsonSerializerOptions;
        _logger = loggerFactory.CreateLogger<MinerUAgentFactory>();

        var githubToken = _configuration["GitHubToken"]
            ?? throw new InvalidOperationException(
                "GitHubToken not found in configuration. " +
                "Please set it using: dotnet user-secrets set GitHubToken \"<your-token>\"");

        _openAiClient = new(
            new System.ClientModel.ApiKeyCredential(githubToken),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://models.inference.ai.azure.com"),
                NetworkTimeout = TimeSpan.FromMinutes(5)
            });

        var apiKey = Environment.GetEnvironmentVariable("MINERU_API_KEY") ?? _configuration["MINERU_API_KEY"];
        var useStandard = string.Equals(
            Environment.GetEnvironmentVariable("MINERU_USE_STANDARD") ?? _configuration["MINERU_USE_STANDARD"],
            "true", StringComparison.OrdinalIgnoreCase);
        _nextjsBaseUrl = Environment.GetEnvironmentVariable("NEXTJS_URL") ?? _configuration["NEXTJS_URL"] ?? "http://localhost:3000";

        _logger.LogInformation("MinerU mode: {Mode}", useStandard ? "standard" : "agent (lightweight)");
        _logger.LogInformation("Next.js URL: {Url}", _nextjsBaseUrl);

        _minerUService = new MinerUCloudService(_httpClientFactory, _logger, apiKey, useStandard);
    }

    public AIAgent CreateAgent()
    {
        var chatClient = _openAiClient.GetChatClient("gpt-4o").AsIChatClient();

        var chatClientAgent = new ChatClientAgent(
            chatClient,
            name: "MinerUAgent",
            description: """
                A document-processing assistant.

                ONLY act on document uploads — do NOT call any tool unless the system message
                explicitly says "The user has uploaded X file(s)".

                When the system message confirms files are uploaded, follow these steps exactly once:
                1. Call parse_documents — extract text from the files via MinerU OCR.
                2. If parse_documents returns "No documents found" or "No content could be extracted",
                   stop immediately and tell the user the extraction failed — do NOT call search_forms or fill_form.
                3. Analyze the extracted content. Identify what type of form is needed (e.g., "job application", "customer info", "survey", etc.).
                   Formulate a concise search query describing the form type.
                4. Call search_forms with that query — returns forms sorted by relevance with similarity score (call this ONCE only).
                   - If results are empty or low similarity (< 0.5), try a different query once.
                5. Match the extracted content to the best fitting form (highest similarity).
                   - If no form matches well, tell the user and stop — do NOT call fill_form.
                6. Call fill_form ONLY with values that are clearly present in the extracted text.
                   - For "text", "number", "email", "tel", "textarea", "select", "radio", "date" fields: use the string value directly.
                   - For "checkbox" fields: use an array of selected option values, or empty array [].
                   - For "list" fields (repeating group): use an array of objects. Each object maps the sub-field IDs to their values. Example:
                     If a list field "Experience" has subFields [{id:"sf_1",type:"text",label:"Company"},{id:"sf_2",type:"text",label:"Role"}],
                     the value should be: [{"sf_1":"Vietbank","sf_2":"Developer"},{"sf_1":"VNPT","sf_2":"Manager"}]
                   - Use empty string "" for simple fields whose value cannot be found.
                   - Use empty array [] for list/checkbox fields whose value cannot be found.
                   - NEVER invent, guess, or fill in placeholder values.
                   - NEVER call fill_form if you have no real extracted content to work with.

                Never call search_forms or fill_form more than once per response (unless search_forms returns low-similarity results, then you may retry once with a different query).
                """,
            tools: [
                AIFunctionFactory.Create(ParseDocumentsAsync, options: new() { Name = "parse_documents", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(SearchFormsAsync, options: new() { Name = "search_forms", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(FillFormAsync, options: new() { Name = "fill_form", SerializerOptions = _jsonSerializerOptions }),
            ]);

        return new MinerUAgent(chatClientAgent, _httpContextAccessor, _logger);
    }

    // =================
    // Tools
    // =================

    [Description("Parse the uploaded documents using MinerU OCR and return the extracted text content.")]
    private async Task<string> ParseDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var files = _httpContextAccessor.HttpContext?.Items["__attachments__"] as List<ExtractedFile> ?? [];
        var docFiles = files.Where(f => f.Bytes.Length > 0).ToList();

        if (docFiles.Count == 0)
            return "No documents found to parse.";

        var parsed = new List<string>();
        foreach (var file in docFiles)
        {
            _logger.LogInformation("Parsing file with MinerU: {MediaType}", file.MediaType);
            try
            {
                var result = await _minerUService.ParseAsync(file.Bytes, GuessFileName(file.MediaType), file.MediaType, cancellationToken);
                if (result is not null)
                    parsed.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MinerU parsing failed for {MediaType}", file.MediaType);
                parsed.Add($"[Parsing failed: {ex.Message}]");
            }
        }

        var text = parsed.Count > 0
            ? string.Join("\n\n---\n\n", parsed)
            : "No content could be extracted from the documents.";
        return text.Length <= 3000 ? text : text[..3000] + "\n\n[truncated]";
    }

    [Description("Search forms by semantic similarity. Analyze the parsed document, then call this with a query describing the form type needed. Returns forms sorted by relevance with a similarity score.")]
    private async Task<List<FormDto>> SearchFormsAsync(
        [Description("Search query describing the type of form needed, based on the document content")] string query,
        CancellationToken cancellationToken = default)
    {
        if (_httpContextAccessor.HttpContext is { } ctx)
        {
            if (ctx.Items.ContainsKey("__forms_searched__"))
            {
                _logger.LogWarning("search_forms called more than once — returning cached result");
                return ctx.Items["__forms_cache__"] as List<FormDto> ?? [];
            }
            ctx.Items["__forms_searched__"] = true;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            var encoded = Uri.EscapeDataString(query);
            var json = await client.GetStringAsync($"{_nextjsBaseUrl}/api/forms?q={encoded}", cancellationToken);
            var forms = JsonSerializer.Deserialize<List<FormDto>>(json) ?? [];
            forms.ForEach(f => f.Embedding = null); // strip embeddings before sending to LLM
            _logger.LogInformation("search_forms returned {Count} results for query: {Query}", forms.Count, query);
            if (_httpContextAccessor.HttpContext is { } c)
                c.Items["__forms_cache__"] = forms;
            return forms;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search forms from {Url}", _nextjsBaseUrl);
            return [];
        }
    }

    [Description("Register the matched form fill result so it can be displayed to the user. Call this after parse_documents and get_forms, exactly once.")]
    private Task<string> FillFormAsync(
        [Description("The ID of the best matching form")] string formId,
        [Description("The display title of the matched form")] string formTitle,
        [Description("JSON object mapping each fieldId to its extracted value. For \"list\" fields (repeating group), the value must be an array of objects where each object maps sub-field IDs to their values; use [] if empty. For \"checkbox\" fields, use an array of strings; use [] if empty. For all other field types, use a string value. Example: {\"field_1\":\"John\", \"field_2\":[{\"sf_a\":\"Vietbank\",\"sf_b\":\"2020\"},{\"sf_a\":\"VNPT\",\"sf_b\":\"2022\"}], \"field_3\":[\"opt1\",\"opt2\"]}")] JsonElement filledValues,
        CancellationToken cancellationToken = default)
    {
        if (_httpContextAccessor.HttpContext is { } ctx)
        {
            var valueDict = filledValues.ValueKind == JsonValueKind.Object
                ? filledValues.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)
                : new Dictionary<string, JsonElement>();

            ctx.Items["__form_fill__"] = new MinerUFormFill
            {
                FormId = formId,
                FormTitle = formTitle,
                FilledValues = valueDict
            };
            _logger.LogInformation("fill_form registered: formId={FormId}", formId);
        }
        return Task.FromResult($"Form fill registered for '{formTitle}'.");
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
}
