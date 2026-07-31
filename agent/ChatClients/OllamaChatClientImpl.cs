using Microsoft.Extensions.AI;

public class OllamaChatClientImpl : IChatClient
{
    private readonly IChatClient _inner;

    public OllamaChatClientImpl(IConfiguration config, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<OllamaChatClientImpl>();
        var model = config["Chat:Model"] ?? "gpt-oss:120b-cloud";
        var baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        logger.LogInformation("Ollama chat client: model={Model} baseUrl={BaseUrl}", model, baseUrl);
        _inner = new OllamaChatClient(new Uri(baseUrl), model);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.GetResponseAsync(chatMessages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.GetStreamingResponseAsync(chatMessages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();
}
