using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class ResOcrParserStrategy : IDocumentParserStrategy
{
    private const int PollIntervalMs = 3000;
    private const int MaxPollAttempts = 120; // 6 minutes max

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly string _lang;

    public ResOcrParserStrategy(IHttpClientFactory httpClientFactory, ILogger logger, string baseUrl, string lang,
        int pollIntervalMs = PollIntervalMs, int maxPollAttempts = MaxPollAttempts)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = baseUrl.TrimEnd('/');
        _lang = lang;
        _pollIntervalMs = pollIntervalMs;
        _maxPollAttempts = maxPollAttempts;
    }

    private readonly int _pollIntervalMs;
    private readonly int _maxPollAttempts;

    public async Task<string?> ParseAsync(byte[] fileBytes, string fileName, string mediaType, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(10);

        // Step 1: Upload file to ResOCR and get the resource id
        _logger.LogInformation("ResOCR [1/3] uploading {FileName} ({Size} bytes) to {Url}", fileName, fileBytes.Length, _baseUrl);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        content.Add(fileContent, "files", fileName);

        var uploadResponse = await client.PostAsync($"{_baseUrl}/v1/resources/upload?lang={_lang}", content, cancellationToken);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadJson = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("ResOCR [1/3] upload response {Status}: {Body}", (int)uploadResponse.StatusCode, uploadJson);

        var uploadResult = JsonSerializer.Deserialize<UploadResponse>(uploadJson);
        var resourceId = uploadResult?.Resources?.FirstOrDefault()?.Id;
        if (resourceId is null)
        {
            _logger.LogError("ResOCR upload returned no resource");
            return null;
        }

        // Step 2: Poll the merge endpoint until OCR has completed for every child task
        _logger.LogInformation("ResOCR [2/3] uploaded as resource {ResourceId}, polling for OCR completion", resourceId);

        MergeResponse? merge = null;
        for (int i = 0; i < _maxPollAttempts; i++)
        {
            await Task.Delay(_pollIntervalMs, cancellationToken);

            var mergeResponse = await client.GetAsync($"{_baseUrl}/v1/resources/{resourceId}/merge", cancellationToken);
            mergeResponse.EnsureSuccessStatusCode();
            merge = JsonSerializer.Deserialize<MergeResponse>(await mergeResponse.Content.ReadAsStringAsync(cancellationToken));

            if (merge is { TaskCount: > 0 } && merge.CompletedTaskCount >= merge.TaskCount)
                break;

            _logger.LogInformation("ResOCR resource {ResourceId} OCR progress: {Completed}/{Total} tasks", resourceId, merge?.CompletedTaskCount ?? 0, merge?.TaskCount ?? 0);
        }

        if (merge is null || merge.CompletedTaskCount <= 0)
        {
            _logger.LogError("ResOCR resource {ResourceId} timed out after {Seconds}s", resourceId, _maxPollAttempts * _pollIntervalMs / 1000);
            return null;
        }

        // Step 3: Merge all pages into a single text document
        _logger.LogInformation("ResOCR [3/3] merging {LineCount} lines from resource {ResourceId}", merge.LineCount, resourceId);

        var text = BuildText(merge.Results);
        _logger.LogInformation("ResOCR extracted {Length} chars from {FileName}", text.Length, fileName);
        return text;
    }

    private static string BuildText(List<MergeLine>? lines)
    {
        if (lines is null || lines.Count == 0)
            return "";

        var pages = new List<string>();
        var currentPage = new List<string>();
        string? currentTaskId = null;

        foreach (var line in lines)
        {
            var text = line.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (currentTaskId is not null && line.TaskId != currentTaskId)
            {
                if (currentPage.Count > 0)
                    pages.Add(string.Join("\n", currentPage));
                currentPage = new List<string>();
            }

            currentTaskId = line.TaskId;
            currentPage.Add(text);
        }

        if (currentPage.Count > 0)
            pages.Add(string.Join("\n", currentPage));

        return string.Join("\n\n---\n\n", pages);
    }

    // ── Models ─────────────────────────────────────────────────────────────────

    private sealed class UploadResponse
    {
        [JsonPropertyName("resources")] public List<UploadResource>? Resources { get; set; }
    }
    private sealed class UploadResource
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }

    private sealed class MergeResponse
    {
        [JsonPropertyName("resource_id")] public string? ResourceId { get; set; }
        [JsonPropertyName("task_count")] public int TaskCount { get; set; }
        [JsonPropertyName("completed_task_count")] public int CompletedTaskCount { get; set; }
        [JsonPropertyName("line_count")] public int LineCount { get; set; }
        [JsonPropertyName("results")] public List<MergeLine>? Results { get; set; }
    }
    private sealed class MergeLine
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("page")] public int? Page { get; set; }
        [JsonPropertyName("task_id")] public string? TaskId { get; set; }
        [JsonPropertyName("task_name")] public string? TaskName { get; set; }
    }
}
