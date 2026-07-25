using System.IO.Compression;
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
    private readonly string? _apiKey;
    private readonly bool _useStandard;

    public MinerUCloudService(IHttpClientFactory httpClientFactory, ILogger logger, string? apiKey, bool useStandard)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = apiKey;
        _useStandard = useStandard;
    }

    public Task<string?> ParseAsync(byte[] fileBytes, string fileName, string mediaType, CancellationToken cancellationToken) =>
        _useStandard
            ? ParseWithStandardApiAsync(fileBytes, fileName, cancellationToken)
            : ParseWithAgentApiAsync(fileBytes, fileName, cancellationToken);

    // ── Agent API (lightweight, no auth, 10 MB limit) ─────────────────────────

    private async Task<string?> ParseWithAgentApiAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();

        _logger.LogInformation("MinerU [agent 1/3] requesting upload URL for {FileName} ({Size} bytes)", fileName, fileBytes.Length);
        var initPayload = JsonSerializer.Serialize(new { file_name = fileName });
        using var initContent = new StringContent(initPayload, Encoding.UTF8, "application/json");
        var initResponse = await client.PostAsync($"{BaseUrl}/api/v1/agent/parse/file", initContent, cancellationToken);
        var initJson = await initResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("MinerU [agent 1/3] response {Status}: {Body}", (int)initResponse.StatusCode, initJson);
        initResponse.EnsureSuccessStatusCode();

        var initResult = JsonSerializer.Deserialize<AgentFileResponse>(initJson);
        var taskId = initResult?.Data?.TaskId;
        var fileUrl = initResult?.Data?.FileUrl;

        if (taskId is null || fileUrl is null)
        {
            _logger.LogError("MinerU [agent 1/3] missing task_id or file_url in response");
            return null;
        }

        _logger.LogInformation("MinerU [agent 2/3] uploading to signed URL (task {TaskId})", taskId);
        using var uploadContent = new ByteArrayContent(fileBytes);
        var uploadResponse = await client.PutAsync(fileUrl, uploadContent, cancellationToken);
        _logger.LogInformation("MinerU [agent 2/3] upload response {Status}", (int)uploadResponse.StatusCode);
        uploadResponse.EnsureSuccessStatusCode();

        _logger.LogInformation("MinerU [agent 3/3] polling task {TaskId}", taskId);

        for (int i = 0; i < MaxPollAttempts; i++)
        {
            await Task.Delay(PollIntervalMs, cancellationToken);
            var pollResponse = await client.GetAsync($"{BaseUrl}/api/v1/agent/parse/{taskId}", cancellationToken);
            pollResponse.EnsureSuccessStatusCode();
            var data = JsonSerializer.Deserialize<AgentPollResponse>(await pollResponse.Content.ReadAsStringAsync(cancellationToken))?.Data;
            _logger.LogInformation("MinerU agent task {TaskId} state: {State}", taskId, data?.State);

            if (data?.State == "done" && data.MarkdownUrl is { } markdownUrl)
                return await client.GetStringAsync(markdownUrl, cancellationToken);

            if (data?.State == "failed")
            {
                _logger.LogError("MinerU agent task {TaskId} failed: {Err}", taskId, data.ErrMsg);
                return null;
            }
        }

        _logger.LogError("MinerU agent task timed out after {Seconds}s", MaxPollAttempts * PollIntervalMs / 1000);
        return null;
    }

    // ── Standard API v4 (auth required, up to 200 MB) ─────────────────────────

    private async Task<string?> ParseWithStandardApiAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(10);
        if (_apiKey is not null)
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        // Step 1: Request signed upload URL
        _logger.LogInformation("MinerU [standard 1/3] requesting upload URL for {FileName} ({Size} bytes)", fileName, fileBytes.Length);
        var initPayload = JsonSerializer.Serialize(new
        {
            files = new[] { new { name = fileName } },
            enable_formula = true,
            enable_table = true,
            language = "ch"
        });
        using var initContent = new StringContent(initPayload, Encoding.UTF8, "application/json");
        var initResponse = await client.PostAsync($"{BaseUrl}/api/v4/file-urls/batch", initContent, cancellationToken);
        var initJson = await initResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("MinerU [standard 1/3] response {Status}: {Body}", (int)initResponse.StatusCode, initJson);
        initResponse.EnsureSuccessStatusCode();

        var initResult = JsonSerializer.Deserialize<BatchFileUrlsResponse>(initJson);
        var batchId = initResult?.Data?.BatchId;
        var fileUrl = initResult?.Data?.FileUrls?.FirstOrDefault();

        if (batchId is null || fileUrl is null)
        {
            _logger.LogError("MinerU [standard 1/3] missing batch_id or file_url in response");
            return null;
        }

        // Step 2: Upload file to signed URL (no auth header, no Content-Type)
        _logger.LogInformation("MinerU [standard 2/3] uploading to signed URL (batch {BatchId})", batchId);
        using var uploadClient = _httpClientFactory.CreateClient();
        using var uploadContent = new ByteArrayContent(fileBytes);
        var uploadResponse = await uploadClient.PutAsync(fileUrl, uploadContent, cancellationToken);
        _logger.LogInformation("MinerU [standard 2/3] upload response {Status}", (int)uploadResponse.StatusCode);
        uploadResponse.EnsureSuccessStatusCode();

        // Step 3: Poll batch results
        _logger.LogInformation("MinerU [standard 3/3] polling batch {BatchId}", batchId);
        for (int i = 0; i < MaxPollAttempts; i++)
        {
            await Task.Delay(PollIntervalMs, cancellationToken);
            var pollResponse = await client.GetAsync($"{BaseUrl}/api/v4/extract-results/batch/{batchId}", cancellationToken);
            pollResponse.EnsureSuccessStatusCode();

            var pollJson = await pollResponse.Content.ReadAsStringAsync(cancellationToken);
            var results = JsonSerializer.Deserialize<BatchResultsResponse>(pollJson)?.Data?.ExtractResult;
            var file = results?.FirstOrDefault();

            _logger.LogInformation("MinerU standard batch {BatchId} state: {State}", batchId, file?.State);

            if (file?.State == "done" && file.FullZipUrl is { } zipUrl)
                return await ExtractMarkdownFromZipAsync(client, zipUrl, cancellationToken);

            if (file?.State == "failed")
            {
                _logger.LogError("MinerU standard batch {BatchId} failed: {Err}", batchId, file.ErrMsg);
                return null;
            }
        }

        _logger.LogError("MinerU standard batch timed out after {Seconds}s", MaxPollAttempts * PollIntervalMs / 1000);
        return null;
    }

    private async Task<string?> ExtractMarkdownFromZipAsync(HttpClient client, string zipUrl, CancellationToken cancellationToken)
    {
        var zipBytes = await client.GetByteArrayAsync(zipUrl, cancellationToken);
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var mdEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        if (mdEntry is null)
        {
            _logger.LogError("MinerU: no .md file found in result ZIP");
            return null;
        }

        using var reader = new StreamReader(mdEntry.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    // ── Models ─────────────────────────────────────────────────────────────────

    private sealed class AgentFileResponse
    {
        [JsonPropertyName("data")] public AgentFileData? Data { get; set; }
    }
    private sealed class AgentFileData
    {
        [JsonPropertyName("task_id")] public string? TaskId { get; set; }
        [JsonPropertyName("file_url")] public string? FileUrl { get; set; }
    }
    private sealed class AgentPollResponse
    {
        [JsonPropertyName("data")] public AgentPollData? Data { get; set; }
    }
    private sealed class AgentPollData
    {
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("markdown_url")] public string? MarkdownUrl { get; set; }
        [JsonPropertyName("err_msg")] public string? ErrMsg { get; set; }
    }

    private sealed class BatchFileUrlsResponse
    {
        [JsonPropertyName("data")] public BatchFileUrlsData? Data { get; set; }
    }
    private sealed class BatchFileUrlsData
    {
        [JsonPropertyName("batch_id")] public string? BatchId { get; set; }
        [JsonPropertyName("file_urls")] public List<string>? FileUrls { get; set; }
    }

    private sealed class BatchResultsResponse
    {
        [JsonPropertyName("data")] public BatchResultsData? Data { get; set; }
    }
    private sealed class BatchResultsData
    {
        [JsonPropertyName("batch_id")] public string? BatchId { get; set; }
        [JsonPropertyName("extract_result")] public List<BatchFileResult>? ExtractResult { get; set; }
    }
    private sealed class BatchFileResult
    {
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("full_zip_url")] public string? FullZipUrl { get; set; }
        [JsonPropertyName("err_msg")] public string? ErrMsg { get; set; }
    }
}
