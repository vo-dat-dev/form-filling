using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024;
});

var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Add(AgentSerializerContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Add(ApiSerializerContext.Default);
});
builder.Services.AddAGUI();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// Shared chat client (single DI-injected IChatClient for all agents)
builder.Services.AddSingleton<IChatClient, OllamaChatClientImpl>();
// builder.Services.AddSingleton<IChatClient, OpenAIChatClientImpl>();

// Database
static string ToNpgsqlConnString(string? url)
{
    if (string.IsNullOrEmpty(url)) return "Host=localhost;Port=5432;Database=form_filling;Username=postgres;Password=postgres";
    if (!url.StartsWith("postgresql://") && !url.StartsWith("postgres://")) return url;
    var u = new Uri(url);
    var userInfo = u.UserInfo?.Split(':');
    var host = u.Host;
    var port = u.IsDefaultPort ? 5432 : u.Port;
    var db = u.AbsolutePath.TrimStart('/');
    var user = userInfo?.Length > 0 ? userInfo[0] : "postgres";
    var pass = userInfo?.Length > 1 ? userInfo[1] : "";
    return $"Host={host};Port={port};Database={db};Username={user};Password={pass}";
}
var rawConn = Environment.GetEnvironmentVariable("DATABASE_URL");
var connString = ToNpgsqlConnString(rawConn);
builder.Services.AddDbContext<FormFillingDbContext>(options =>
    options.UseNpgsql(connString));
builder.Services.AddScoped<DbService>();

// Expose the configured JsonSerializerOptions so factories can inject it
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions);

// Register document parser strategy (Strategy pattern)
var parserStrategy = Environment.GetEnvironmentVariable("DOCUMENT_PARSER_STRATEGY") ?? "ocr";
switch (parserStrategy.ToLowerInvariant())
{
    case "ocr":
        var ocrUrl = Environment.GetEnvironmentVariable("OCR_URL") ?? "http://localhost:8091";
        builder.Services.AddSingleton<IDocumentParserStrategy>(sp =>
            new OcrServiceParserStrategy(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("OcrServiceParserStrategy"),
                ocrUrl));
        break;

    default: // "mineru"
        builder.Services.AddSingleton<IDocumentParserStrategy>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("MinerUCloudParserStrategy");
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var config = sp.GetRequiredService<IConfiguration>();

            var apiKey = Environment.GetEnvironmentVariable("MINERU_API_KEY") ?? config["MINERU_API_KEY"];
            var useStandard = string.Equals(
                Environment.GetEnvironmentVariable("MINERU_USE_STANDARD") ?? config["MINERU_USE_STANDARD"],
                "true", StringComparison.OrdinalIgnoreCase);

            var inner = new MinerUCloudService(httpClientFactory, logger, apiKey, useStandard);
            return new MinerUCloudParserStrategy(inner);
        });
        break;
}

// Register agent factories — DI injects all dependencies automatically
builder.Services.AddSingleton<IAgentFactory, ProverbsAgentFactory>();
builder.Services.AddSingleton<IAgentFactory, MinerUAgentFactory>();

WebApplication app = builder.Build();

app.UseMiddleware<FileAttachmentMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/health/mineru", async (IHttpClientFactory factory, ILoggerFactory loggers) =>
{
    var logger = loggers.CreateLogger("MinerUHealth");
    using var client = factory.CreateClient();
    try
    {
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
        return Results.Json(new { reachable = false, error = ex.Message }, statusCode: 503);
    }
});

// Mark migration as applied on startup (safe for existing DB)
try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<FormFillingDbContext>();
        await db.Database.MigrateAsync();
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Database migration skipped");
}

// ---- Data API endpoints ----

var api = app.MapGroup("/api");

// Threads
api.MapGet("/threads", async (DbService db, string? agentId) =>
    Results.Ok(await db.ListThreads(agentId ?? "minerU")));

api.MapPost("/threads", async (DbService db, CreateThreadRequest body) =>
{
    var thread = await db.CreateThread(body.AgentId ?? "minerU", body.Title ?? "New Conversation");
    return Results.Ok(thread);
});

api.MapPatch("/threads/{id}", async (DbService db, string id, UpdateThreadRequest body) =>
{
    var thread = await db.UpdateThread(id, body.Title, body.Metadata);
    return thread != null ? Results.Ok(thread) : Results.NotFound();
});

api.MapDelete("/threads/{id}", async (DbService db, string id) =>
{
    var ok = await db.DeleteThread(id);
    return ok ? Results.Ok(new { success = true }) : Results.NotFound();
});

// Forms
api.MapGet("/forms", async (DbService db, string? q) =>
{
    if (!string.IsNullOrWhiteSpace(q))
        return Results.Ok(await db.ListForms(q));
    return Results.Ok(await db.ListForms());
});

api.MapGet("/forms/{id}", async (DbService db, string id) =>
{
    var form = await db.GetForm(id);
    return form != null ? Results.Ok(form) : Results.NotFound();
});

api.MapPost("/forms", async (DbService db, CreateFormRequest body) =>
{
    var form = await db.CreateForm(body.Title, body.Description, body.Fields, body.Embedding);
    return Results.Ok(form);
});

api.MapPut("/forms/{id}", async (DbService db, string id, UpdateFormRequest body) =>
{
    var existing = await db.GetForm(id);
    if (existing == null) return Results.NotFound();

    string? newEmbedding = body.Embedding switch
    {
        null when body.DescriptionChanged => "",
        not null => body.Embedding,
        _ => null,
    };

    var form = await db.UpdateForm(id, body.Title, body.Description, body.Fields, newEmbedding);
    return form != null ? Results.Ok(form) : Results.NotFound();
});

api.MapDelete("/forms/{id}", async (DbService db, string id) =>
{
    var ok = await db.DeleteForm(id);
    return ok ? Results.Ok(new { success = true }) : Results.NotFound();
});

// Submissions
api.MapGet("/forms/{formId}/submissions", async (DbService db, string formId) =>
    Results.Ok(await db.ListSubmissions(formId)));

api.MapPost("/forms/{formId}/submissions", async (DbService db, string formId, JsonElement body) =>
{
    var submission = await db.CreateSubmission(formId, body.GetRawText());
    return submission != null ? Results.Ok(submission) : Results.NotFound();
});

api.MapGet("/submissions/{id}", async (DbService db, string id) =>
{
    var submission = await db.GetSubmission(id);
    return submission != null ? Results.Ok(submission) : Results.NotFound();
});

// Scan all IAgentFactory implementations and map their routes
foreach (var factory in app.Services.GetServices<IAgentFactory>())
    app.MapAGUI(factory.Route, factory.CreateAgent());

await app.RunAsync();

public partial class Program { }

// ---- Request DTOs ----

public record CreateThreadRequest(string? AgentId, string? Title);
public record UpdateThreadRequest(string? Title, string? Metadata);
public record CreateFormRequest(string Title, string? Description, string Fields, string? Embedding);
public record UpdateFormRequest(string Title, string? Description, string Fields, string? Embedding, bool DescriptionChanged = false);

// Serializer context covering types from all agent factories
[JsonSerializable(typeof(ProverbsStateSnapshot))]
[JsonSerializable(typeof(WeatherInfo))]
[JsonSerializable(typeof(List<FormDto>))]
[JsonSerializable(typeof(FormDto))]
[JsonSerializable(typeof(MinerUFormFillList))]
internal sealed partial class AgentSerializerContext : JsonSerializerContext;
