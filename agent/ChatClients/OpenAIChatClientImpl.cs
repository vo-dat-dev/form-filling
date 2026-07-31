using Microsoft.Extensions.AI;
using OpenAI;

public class OpenAIChatClientImpl : IChatClient
{
    private readonly IChatClient _inner;

    public OpenAIChatClientImpl(IConfiguration config, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<OpenAIChatClientImpl>();
        var model = config["Chat:Model"] ?? "gpt-4o-mini";
        var apiKey = config["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "OpenAI API key not found in configuration. " +
                "Please set it using: dotnet user-secrets set \"OpenAI:ApiKey\" \"<your-key>\" " +
                "or export OPENAI_API_KEY");
        var endpoint = config["OpenAI:BaseUrl"] ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1";
        logger.LogInformation("OpenAI chat client: model={Model} endpoint={Endpoint}", model, endpoint);
        var client = new OpenAIClient(
            new System.ClientModel.ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint),
                NetworkTimeout = TimeSpan.FromMinutes(5)
            });
        _inner = client.GetChatClient(model).AsIChatClient();
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
