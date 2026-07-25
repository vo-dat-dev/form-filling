using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class MinerUCloudService
{
    private const string BaseUrl = "https://mineru.net";
    private const int PollIntervalMs = 3000;
    private const int MaxPollAttempts = 60; // 3 minutes max

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public MinerUCloudService(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> ParseAsync(byte[] fileBytes, string fileName, string mediaType, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();

        // Step 1: Request signed upload URL + task_id
        _logger.LogInformation("MinerU [1/3] requesting upload URL for {FileName} ({Size} bytes)", fileName, fileBytes.Length);
        var initPayload = JsonSerializer.Serialize(new { file_name = fileName });
        using var initContent = new StringContent(initPayload, Encoding.UTF8, "application/json");
        var initResponse = await client.PostAsync($"{BaseUrl}/api/v1/agent/parse/file", initContent, cancellationToken);
        var initJson = await initResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("MinerU [1/3] response {Status}: {Body}", (int)initResponse.StatusCode, initJson);
        initResponse.EnsureSuccessStatusCode();

        var initResult = JsonSerializer.Deserialize<AgentFileResponse>(initJson);
        var taskId = initResult?.Data?.TaskId;
        var fileUrl = initResult?.Data?.FileUrl;

        if (taskId is null || fileUrl is null)
        {
            _logger.LogError("MinerU [1/3] missing task_id or file_url in response");
            return null;
        }

        // Step 2: Upload file bytes to signed URL
        _logger.LogInformation("MinerU [2/3] uploading to signed URL (task {TaskId})", taskId);
        using var uploadContent = new ByteArrayContent(fileBytes);
        // OSS pre-signed URLs are generated with application/octet-stream; setting a different
        // Content-Type causes a 403. Let ByteArrayContent default to octet-stream.
        var uploadResponse = await client.PutAsync(fileUrl, uploadContent, cancellationToken);
        _logger.LogInformation("MinerU [2/3] upload response {Status}", (int)uploadResponse.StatusCode);
        uploadResponse.EnsureSuccessStatusCode();

        _logger.LogInformation("MinerU [3/3] polling task {TaskId}", taskId);

        // Step 3: Poll until state is done or failed
        for (int i = 0; i < MaxPollAttempts; i++)
        {
            await Task.Delay(PollIntervalMs, cancellationToken);

            var pollResponse = await client.GetAsync($"{BaseUrl}/api/v1/agent/parse/{taskId}", cancellationToken);
            pollResponse.EnsureSuccessStatusCode();

            var pollJson = await pollResponse.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<AgentPollResponse>(pollJson)?.Data;

            _logger.LogInformation("MinerU task {TaskId} state: {State}", taskId, data?.State);

            if (data?.State == "done" && data.MarkdownUrl is { } markdownUrl)
                return await client.GetStringAsync(markdownUrl, cancellationToken);

            if (data?.State == "failed")
            {
                _logger.LogError("MinerU task {TaskId} failed: {Err}", taskId, data.ErrMsg);
                return null;
            }
        }

        _logger.LogError("MinerU task {TaskId} timed out after {Seconds}s", taskId, MaxPollAttempts * PollIntervalMs / 1000);
        return null;
    }

    private sealed class AgentFileResponse
    {
        [JsonPropertyName("data")]
        public AgentFileData? Data { get; set; }
    }

    private sealed class AgentFileData
    {
        [JsonPropertyName("task_id")]
        public string? TaskId { get; set; }

        [JsonPropertyName("file_url")]
        public string? FileUrl { get; set; }
    }

    private sealed class AgentPollResponse
    {
        [JsonPropertyName("data")]
        public AgentPollData? Data { get; set; }
    }

    private sealed class AgentPollData
    {
        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("markdown_url")]
        public string? MarkdownUrl { get; set; }

        [JsonPropertyName("err_msg")]
        public string? ErrMsg { get; set; }
    }
}
