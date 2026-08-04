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

        try
        {
            using var client = httpClientFactory.CreateClient();
            var payload = new { model = _embeddingModel, input = text.Trim() };
            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8,
                "application/json");

            var res = await client.PostAsync($"{_ollamaBaseUrl}/api/embed", content, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("Embedding request failed: {Status}", (int)res.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("embeddings", out var embeddings) ||
                embeddings.ValueKind != JsonValueKind.Array ||
                embeddings.GetArrayLength() == 0)
            {
                logger.LogWarning("Embedding response has unexpected format");
                return null;
            }

            var vector = embeddings[0];
            var values = new float[vector.GetArrayLength()];
            var i = 0;
            foreach (var v in vector.EnumerateArray())
                values[i++] = v.GetSingle();

            logger.LogInformation("Embedded text with {Model}: {Dim} dims", _embeddingModel, values.Length);
            return new Vector(new ReadOnlyMemory<float>(values));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding failed");
            return null;
        }
    }
}
