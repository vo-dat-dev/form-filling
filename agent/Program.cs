using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using System.ComponentModel;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // 50 MB max file; base64 adds ~33% overhead, add JSON wrapper headroom → 100 MB
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Add(ProverbsAgentSerializerContext.Default));
builder.Services.AddAGUI();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

WebApplication app = builder.Build();

// Create the agent factory and map the AG-UI agent endpoint
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var jsonOptions = app.Services.GetRequiredService<IOptions<JsonOptions>>();
var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();
var httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
var agentFactory = new ProverbsAgentFactory(builder.Configuration, loggerFactory, jsonOptions.Value.SerializerOptions);
var minerUFactory = new MinerUAgentFactory(builder.Configuration, loggerFactory, httpClientFactory, httpContextAccessor, jsonOptions.Value.SerializerOptions);

app.UseMiddleware<FileAttachmentMiddleware>();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/health/mineru", async (IHttpClientFactory factory, ILoggerFactory loggers) =>
{
    var logger = loggers.CreateLogger("MinerUHealth");
    using var client = factory.CreateClient();
    try
    {
        // Hit a non-existent task — mineru.net returns 4xx (not a network error),
        // which proves we can reach the cloud API.
        var res = await client.GetAsync("https://mineru.net/api/v1/agent/parse/__health_check__");
        var body = await res.Content.ReadAsStringAsync();
        logger.LogInformation("MinerU health: {Status} {Body}", (int)res.StatusCode, body);
        return Results.Ok(new
        {
            reachable = true,
            httpStatus = (int)res.StatusCode,
            note = "4xx from mineru.net means network is fine; API requires a valid task_id"
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "MinerU health check failed");
        return Results.Json(new { reachable = false, error = ex.Message },
            statusCode: 503);
    }
});
app.MapAGUI("/", agentFactory.CreateProverbsAgent());
app.MapAGUI("/minerU", minerUFactory.CreateMinerUAgent());

await app.RunAsync();

// =================
// State Management
// =================
public class ProverbsState
{
    public List<string> Proverbs { get; set; } = [];
}

// =================
// Agent Factory
// =================
public class ProverbsAgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly ProverbsState _state;
    private readonly OpenAIClient _openAiClient;
    private readonly ILogger _logger;
    private readonly System.Text.Json.JsonSerializerOptions _jsonSerializerOptions;

    public ProverbsAgentFactory(IConfiguration configuration, ILoggerFactory loggerFactory, System.Text.Json.JsonSerializerOptions jsonSerializerOptions)
    {
        _configuration = configuration;
        _state = new();
        _logger = loggerFactory.CreateLogger<ProverbsAgentFactory>();
        _jsonSerializerOptions = jsonSerializerOptions;

        // Get the GitHub token from configuration
        var githubToken = _configuration["GitHubToken"]
            ?? throw new InvalidOperationException(
                "GitHubToken not found in configuration. " +
                "Please set it using: dotnet user-secrets set GitHubToken \"<your-token>\" " +
                "or get it using: gh auth token");

        _openAiClient = new(
            new System.ClientModel.ApiKeyCredential(githubToken),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://models.inference.ai.azure.com")
            });
    }

    public AIAgent CreateProverbsAgent()
    {
        var chatClient = _openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient();

        var chatClientAgent = new ChatClientAgent(
            chatClient,
            name: "ProverbsAgent",
            description: @"A helpful assistant that helps manage and discuss proverbs.
            You have tools available to add, set, or retrieve proverbs from the list.
            When discussing proverbs, ALWAYS use the get_proverbs tool to see the current list before mentioning, updating, or discussing proverbs with the user.",
            tools: [
                AIFunctionFactory.Create(GetProverbs, options: new() { Name = "get_proverbs", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(AddProverbs, options: new() { Name = "add_proverbs", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(SetProverbs, options: new() { Name = "set_proverbs", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(GetWeather, options: new() { Name = "get_weather", SerializerOptions = _jsonSerializerOptions })
            ]);

        return new SharedStateAgent(chatClientAgent, _jsonSerializerOptions);
    }

    // =================
    // Tools
    // =================

    [Description("Get the current list of proverbs.")]
    private List<string> GetProverbs()
    {
        _logger.LogInformation("📖 Getting proverbs: {Proverbs}", string.Join(", ", _state.Proverbs));
        return _state.Proverbs;
    }

    [Description("Add new proverbs to the list.")]
    private void AddProverbs([Description("The proverbs to add")] List<string> proverbs)
    {
        _logger.LogInformation("➕ Adding proverbs: {Proverbs}", string.Join(", ", proverbs));
        _state.Proverbs.AddRange(proverbs);
    }

    [Description("Replace the entire list of proverbs.")]
    private void SetProverbs([Description("The new list of proverbs")] List<string> proverbs)
    {
        _logger.LogInformation("📝 Setting proverbs: {Proverbs}", string.Join(", ", proverbs));
        _state.Proverbs = [.. proverbs];
    }

    [Description("Get the weather for a given location. Ensure location is fully spelled out.")]
    private WeatherInfo GetWeather([Description("The location to get the weather for")] string location)
    {
        _logger.LogInformation("🌤️  Getting weather for: {Location}", location);
        return new()
        {
            Temperature = 20,
            Conditions = "sunny",
            Humidity = 50,
            WindSpeed = 10,
            FeelsLike = 25
        };
    }
}

// =================
// Data Models
// =================

public class ProverbsStateSnapshot
{
    [JsonPropertyName("proverbs")]
    public List<string> Proverbs { get; set; } = [];
}

public class WeatherInfo
{
    [JsonPropertyName("temperature")]
    public int Temperature { get; init; }

    [JsonPropertyName("conditions")]
    public string Conditions { get; init; } = string.Empty;

    [JsonPropertyName("humidity")]
    public int Humidity { get; init; }

    [JsonPropertyName("wind_speed")]
    public int WindSpeed { get; init; }

    [JsonPropertyName("feelsLike")]
    public int FeelsLike { get; init; }
}

public partial class Program { }

// =================
// MinerU Agent Factory
// =================
public class MinerUAgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly System.Text.Json.JsonSerializerOptions _jsonSerializerOptions;
    private readonly OpenAIClient _openAiClient;
    private readonly ILogger _logger;
    private readonly string _nextjsBaseUrl;
    private readonly MinerUCloudService _minerUService;

    public MinerUAgentFactory(IConfiguration configuration, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor, System.Text.Json.JsonSerializerOptions jsonSerializerOptions)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
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
                Endpoint = new Uri(Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://models.inference.ai.azure.com")
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

    public AIAgent CreateMinerUAgent()
    {
        var chatClient = _openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient();

        var chatClientAgent = new ChatClientAgent(
            chatClient,
            name: "MinerUAgent",
            description: """
                A document-processing assistant.

                ONLY act on document uploads — do NOT call any tool unless the system message
                explicitly says "The user has uploaded X file(s)".

                When the system message confirms files are uploaded, follow these steps exactly once:
                1. Call parse_documents — extract text from the files via MinerU OCR.
                2. Call get_forms — retrieve available forms (call this ONCE only).
                3. Match the extracted content to the best fitting form, determine each field value.
                4. Call fill_form with the formId, formTitle, and a JSON object of fieldId→value pairs.

                Never call get_forms or fill_form more than once per response.
                Never call fill_form if parse_documents returned no content.
                """,
            tools: [
                AIFunctionFactory.Create(ParseDocumentsAsync, options: new() { Name = "parse_documents", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(GetFormsAsync, options: new() { Name = "get_forms", SerializerOptions = _jsonSerializerOptions }),
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
                var fileName = GuessFileName(file.MediaType);
                var result = await _minerUService.ParseAsync(file.Bytes, fileName, file.MediaType, cancellationToken);
                if (result is not null)
                    parsed.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MinerU parsing failed for {MediaType}", file.MediaType);
                parsed.Add($"[Parsing failed: {ex.Message}]");
            }
        }

        return parsed.Count > 0
            ? string.Join("\n\n---\n\n", parsed)
            : "No content could be extracted from the documents.";
    }

    [Description("Get all available forms from the system. Call this ONCE per response after parse_documents has returned content.")]
    private async Task<List<FormDto>> GetFormsAsync(CancellationToken cancellationToken = default)
    {
        // Guard: prevent the LLM from calling this more than once per HTTP request
        if (_httpContextAccessor.HttpContext is { } ctx)
        {
            if (ctx.Items.ContainsKey("__forms_fetched__"))
            {
                _logger.LogWarning("get_forms called more than once — returning cached result");
                return ctx.Items["__forms_cache__"] as List<FormDto> ?? [];
            }
            ctx.Items["__forms_fetched__"] = true;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            var json = await client.GetStringAsync($"{_nextjsBaseUrl}/api/forms", cancellationToken);
            var forms = System.Text.Json.JsonSerializer.Deserialize<List<FormDto>>(json) ?? [];
            if (_httpContextAccessor.HttpContext is { } c)
                c.Items["__forms_cache__"] = forms;
            return forms;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch forms from {Url}", _nextjsBaseUrl);
            return [];
        }
    }

    [Description("Register the matched form fill result so it can be displayed to the user. Call this after parse_documents and get_forms, exactly once.")]
    private Task<string> FillFormAsync(
        [Description("The ID of the best matching form")] string formId,
        [Description("The display title of the matched form")] string formTitle,
        [Description("JSON object mapping each fieldId to its extracted value, e.g. {\"field1\": \"value1\"}")] System.Text.Json.JsonElement filledValues,
        CancellationToken cancellationToken = default)
    {
        if (_httpContextAccessor.HttpContext is { } ctx)
        {
            var valueDict = filledValues.ValueKind == System.Text.Json.JsonValueKind.Object
                ? filledValues.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)
                : new Dictionary<string, System.Text.Json.JsonElement>();

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

// =================
// Serializer Context
// =================
[JsonSerializable(typeof(ProverbsStateSnapshot))]
[JsonSerializable(typeof(WeatherInfo))]
[JsonSerializable(typeof(List<FormDto>))]
[JsonSerializable(typeof(FormDto))]
internal sealed partial class ProverbsAgentSerializerContext : JsonSerializerContext;
