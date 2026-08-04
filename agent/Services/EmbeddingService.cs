using OllamaSharp;
using OllamaSharp.Models;
using Pgvector;

public class EmbeddingService(
    IConfiguration configuration,
    ILogger<EmbeddingService> logger)
{
    private readonly string _ollamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
        ?? configuration["OLLAMA_BASE_URL"]
        ?? "http://localhost:11434";

    private readonly string _embeddingModel = Environment.GetEnvironmentVariable("EMBEDDING_MODEL")
        ?? configuration["EMBEDDING_MODEL"]
        ?? "bge-m3";

    private OllamaApiClient GetClient()
    {
        var client = new OllamaApiClient(new Uri(_ollamaBaseUrl));
        client.SelectedModel = _embeddingModel;
        return client;
    }

    public async Task<Vector?> EmbedAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var vectors = await EmbedBatchAsync([text], ct);
        return vectors?.Count > 0 ? vectors[0] : null;
    }

    /// <summary>
    /// Embeds a batch of texts using OllamaSharp (model bge-m3, 1024 dims).
    /// Returns a Vector per input aligned by index; entries for empty inputs are null.
    /// </summary>
    public async Task<List<Vector>?> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var items = texts.Select(t => t?.Trim() ?? "").ToList();
        if (items.Count == 0 || items.All(string.IsNullOrEmpty)) return null;

        try
        {
            var ollama = GetClient();
            var request = new EmbedRequest
            {
                Model = _embeddingModel,
                Input = [.. items]
            };

            var response = await ollama.EmbedAsync(request, ct);
            if (response?.Embeddings == null || response.Embeddings.Count == 0)
            {
                logger.LogWarning("Embedding response is empty");
                return null;
            }

            var vectors = new List<Vector?>();
            foreach (var embedding in response.Embeddings)
            {
                if (embedding != null && embedding.Length > 0)
                {
                    vectors.Add(new Vector(new ReadOnlyMemory<float>(embedding)));
                }
                else
                {
                    vectors.Add(null);
                }
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

            logger.LogInformation("Embedded {Count} texts with {Model} via OllamaSharp", items.Count, _embeddingModel);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Batch embedding failed");
            return null;
        }
    }
}
