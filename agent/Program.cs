using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Pgvector;
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
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connString, npgsql => npgsql.UseVector()));
builder.Services.AddScoped<IApplicationDbContext>(
    provider => provider.GetRequiredService<ApplicationDbContext>());
builder.Services.AddScoped<DbService>();
builder.Services.AddSingleton<EmbeddingService>();

// Expose the configured JsonSerializerOptions so factories can inject it
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions);

// Register document parser strategy (Strategy pattern)
var parserStrategy = Environment.GetEnvironmentVariable("DOCUMENT_PARSER_STRATEGY") ?? "resocr";
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

    case "resocr":
        var resOcrUrl = Environment.GetEnvironmentVariable("RESOCR_URL") ?? "http://localhost:8001";
        var resOcrLang = Environment.GetEnvironmentVariable("RESOCR_LANG") ?? "vi";
        builder.Services.AddSingleton<IDocumentParserStrategy>(sp =>
            new ResOcrParserStrategy(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("ResOcrParserStrategy"),
                resOcrUrl,
                resOcrLang));
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
builder.Services.AddSingleton<IAgentFactory, FormFillAgentFactory>();

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
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Database migration skipped");
}

// ---- Data API endpoints (EndpointGroupBase pattern) ----
app.MapEndpoints();

// Scan all IAgentFactory implementations and map their routes
foreach (var factory in app.Services.GetServices<IAgentFactory>())
    app.MapAGUI(factory.Route, factory.CreateAgent());

await app.RunAsync();

public partial class Program
{
    internal static Vector? ParseVector(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return new Vector(s); }
        catch { return null; }
    }
}

// ---- Request DTOs ----

public record CreateThreadRequest(string? AgentId, string? Title);
public record UpdateThreadRequest(string? Title, string? Metadata);
public record CreateFormRequest(string Title, string? Description, string Fields);
public record UpdateFormRequest(string Title, string? Description, string Fields);

// Serializer context covering types from all agent factories
[JsonSerializable(typeof(List<FormDto>))]
[JsonSerializable(typeof(FormDto))]
[JsonSerializable(typeof(FormFillResultList))]
internal sealed partial class AgentSerializerContext : JsonSerializerContext;