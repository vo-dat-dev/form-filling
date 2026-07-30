using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001 // ContinuationToken is evaluation-only
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by ProverbsAgentFactory")]
internal sealed class SharedStateAgent : DelegatingAIAgent
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public SharedStateAgent(AIAgent innerAgent, JsonSerializerOptions jsonSerializerOptions)
        : base(innerAgent)
    {
        _jsonSerializerOptions = jsonSerializerOptions;
    }

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        return RunCoreStreamingAsync(messages, thread, options, cancellationToken).ToAgentResponseAsync(cancellationToken);
    }

    private static List<ChatMessage> TrimHistory(IEnumerable<ChatMessage> messages, int maxMessages = 10)
    {
        var list = messages.ToList();
        var systemIdx = list.FindIndex(m => m.Role == ChatRole.System);
        if (systemIdx < 0) return list.Count > maxMessages ? list[^maxMessages..] : list;

        var system = list[systemIdx];
        var rest = list[(systemIdx + 1)..];
        return rest.Count <= maxMessages - 1
            ? list
            : [system, .. rest[^(maxMessages - 1)..]];
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        messages = TrimHistory(messages);

        if (options is not ChatClientAgentRunOptions { ChatOptions.AdditionalProperties: { } properties } chatRunOptions ||
            !properties.TryGetValue("ag_ui_state", out JsonElement state))
        {
            await foreach (var update in InnerAgent.RunStreamingAsync(messages, thread, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
            yield break;
        }

        var firstRunOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = chatRunOptions.ChatOptions.Clone(),
            AllowBackgroundResponses = chatRunOptions.AllowBackgroundResponses,
            ContinuationToken = chatRunOptions.ContinuationToken,
            ChatClientFactory = chatRunOptions.ChatClientFactory,
        };

        // Configure JSON schema response format for structured state output
        firstRunOptions.ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<ProverbsStateSnapshot>(
            schemaName: "ProverbsStateSnapshot",
            schemaDescription: "A response containing the current list of proverbs");

        ChatMessage stateUpdateMessage = new(
            ChatRole.System,
            [
                new TextContent("Here is the current state in JSON format:"),
                new TextContent(state.GetRawText()),
                new TextContent("The new state is:")
            ]);

        var firstRunMessages = messages.Append(stateUpdateMessage);

        var allUpdates = new List<AgentResponseUpdate>();
        await foreach (var update in InnerAgent.RunStreamingAsync(firstRunMessages, thread, firstRunOptions, cancellationToken).ConfigureAwait(false))
        {
            allUpdates.Add(update);

            // Yield all non-text updates (tool calls, etc.)
            bool hasNonTextContent = update.Contents.Any(c => c is not TextContent);
            if (hasNonTextContent)
            {
                yield return update;
            }
        }

        var response = allUpdates.ToAgentResponse();

        JsonElement? stateSnapshot = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<DataContent>()
            .Where(d => d.MediaType == "application/json")
            .Select(d =>
            {
                var bytes = d.Data.IsEmpty && d.Uri is { } uri
                    ? Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..])
                    : d.Data.ToArray();
                return JsonSerializer.Deserialize<JsonElement>(bytes, _jsonSerializerOptions);
            })
            .LastOrDefault();

        if (stateSnapshot is { ValueKind: not JsonValueKind.Undefined } snap)
        {
            byte[] stateBytes = JsonSerializer.SerializeToUtf8Bytes(
                snap,
                _jsonSerializerOptions.GetTypeInfo(typeof(JsonElement)));
            yield return new AgentResponseUpdate
            {
                Contents = [new DataContent(stateBytes, "application/json")]
            };
        }
        else
        {
            yield break;
        }

        var secondRunMessages = messages.Concat(response.Messages).Append(
            new ChatMessage(
                ChatRole.System,
                [new TextContent("Please provide a concise summary of the state changes in at most two sentences.")]));

        await foreach (var update in InnerAgent.RunStreamingAsync(secondRunMessages, thread, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }
}
