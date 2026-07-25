using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using System.ComponentModel;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Add(ProverbsAgentSerializerContext.Default));
builder.Services.AddAGUI();
builder.Services.AddHttpClient();

WebApplication app = builder.Build();

// Create the agent factory and map the AG-UI agent endpoint
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var jsonOptions = app.Services.GetRequiredService<IOptions<JsonOptions>>();
var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();
var agentFactory = new ProverbsAgentFactory(builder.Configuration, loggerFactory, jsonOptions.Value.SerializerOptions);
var minerUFactory = new MinerUAgentFactory(builder.Configuration, loggerFactory, httpClientFactory, jsonOptions.Value.SerializerOptions);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
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
    private readonly System.Text.Json.JsonSerializerOptions _jsonSerializerOptions;
    private readonly OpenAIClient _openAiClient;

    public MinerUAgentFactory(IConfiguration configuration, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, System.Text.Json.JsonSerializerOptions jsonSerializerOptions)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
        _jsonSerializerOptions = jsonSerializerOptions;

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
    }

    public AIAgent CreateMinerUAgent()
    {
        var chatClient = _openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient();
        var logger = _loggerFactory.CreateLogger<MinerUAgentFactory>();

        var chatClientAgent = new ChatClientAgent(
            chatClient,
            name: "MinerUAgent",
            description: "A document-processing assistant. When the user uploads a file, it is parsed by MinerU and the extracted content is used to answer questions.");

        var minerUBaseUrl = _configuration["MinerUUrl"]
            ?? Environment.GetEnvironmentVariable("MINERÚ_URL")
            ?? "http://localhost:8090";

        return new MinerUAgent(chatClientAgent, _httpClientFactory, minerUBaseUrl, logger);
    }
}

// =================
// Serializer Context
// =================
[JsonSerializable(typeof(ProverbsStateSnapshot))]
[JsonSerializable(typeof(WeatherInfo))]
internal sealed partial class ProverbsAgentSerializerContext : JsonSerializerContext;
