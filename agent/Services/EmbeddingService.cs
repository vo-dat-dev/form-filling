using Pgvector;
using System.Text.Json;

public class EmbeddingService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<EmbeddingService> logger)
{
    private readonly string _ollamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
        ?? configuration["OLLAMA_BASE_URL"]
        ?? "http://localhost:11434";

    private readonly string _embeddingModel = Environment.GetEnvironmentVariable("EMBEDDING_MODEL")
        ?? configuration["EMBEDDING_MODEL"]
        ?? "bge-m3";

    public async Task<Vector?> EmbedAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var vectors = await EmbedBatchAsync([text], ct);
        return vectors?.Count > 0 ? vectors[0] : null;
    }

    /// <summary>
    /// Embeds a batch of texts in a single Ollama /api/embed call (model bge-m3,
    /// 1024 dims). Returns a Vector per input aligned by index; entries for empty
    /// inputs are null.
    /// </summary>
    public async Task<List<Vector>?> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var items = texts.Select(t => t?.Trim() ?? "").ToList();
        if (items.Count == 0 || items.All(string.IsNullOrEmpty)) return null;

        var payload = new { model = _embeddingModel, input = items };
        try
        {
            using var client = httpClientFactory.CreateClient();
            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8,
                "application/json");

            var res = await client.PostAsync($"{_ollamaBaseUrl}/api/embed", content, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("Batch embedding request failed: {Status}", (int)res.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("embeddings", out var embeddings) ||
                embeddings.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("Batch embedding response has unexpected format");
                return null;
            }

            var vectors = new List<Vector?>(items.Count);
            foreach (var vec in embeddings.EnumerateArray())
            {
                var values = new float[vec.GetArrayLength()];
                var i = 0;
                foreach (var v in vec.EnumerateArray())
                    values[i++] = v.GetSingle();
                vectors.Add(new Vector(new ReadOnlyMemory<float>(values)));
            }

            // Keep alignment with the requested inputs (empty inputs have no vector).
            var result = new List<Vector>(items.Count);
            var offset = 0;
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item))
                {
                    result.Add(null!);
                    continue;
                }
                if (offset < vectors.Count && vectors[offset] is not null)
                    result.Add(vectors[offset]!);
                else
                    result.Add(null!);
                offset++;
            }

            logger.LogInformation("Embedded {Count} texts with {Model}", items.Count, _embeddingModel);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Batch embedding failed");
            return null;
        }
    }
}
