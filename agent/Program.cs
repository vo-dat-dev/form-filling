using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // 50 MB max file; base64 adds ~33% overhead, add JSON wrapper headroom → 100 MB
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Add(AgentSerializerContext.Default));
builder.Services.AddAGUI();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// Expose the configured JsonSerializerOptions so factories can inject it
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions);

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

// Scan all IAgentFactory implementations and map their routes
foreach (var factory in app.Services.GetServices<IAgentFactory>())
    app.MapAGUI(factory.Route, factory.CreateAgent());

await app.RunAsync();

public partial class Program { }

// Serializer context covering types from all agent factories
[JsonSerializable(typeof(ProverbsStateSnapshot))]
[JsonSerializable(typeof(WeatherInfo))]
[JsonSerializable(typeof(List<FormDto>))]
[JsonSerializable(typeof(FormDto))]
internal sealed partial class AgentSerializerContext : JsonSerializerContext;
