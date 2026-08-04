#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Pgvector;
using System.ComponentModel;
using System.Text.Json;

public class MinerUAgentFactory : IAgentFactory
{
  public string Route => "/minerU";

  private readonly IConfiguration _configuration;
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly IHttpContextAccessor _httpContextAccessor;
  private readonly JsonSerializerOptions _jsonSerializerOptions;
  private readonly IChatClient _chatClient;
  private readonly ILogger _logger;
  private readonly string _ollamaBaseUrl;
  private readonly string _embeddingModel;
  private readonly IDocumentParserStrategy _parserStrategy;
  private readonly EmbeddingService _embeddings;
  private readonly bool _chunkEnabled;
  private readonly int _parentChunkSize;
  private readonly int _childChunkSize;

  public MinerUAgentFactory(
      IConfiguration configuration,
      IChatClient chatClient,
      ILoggerFactory loggerFactory,
      IHttpClientFactory httpClientFactory,
      IHttpContextAccessor httpContextAccessor,
      JsonSerializerOptions jsonSerializerOptions,
      IDocumentParserStrategy parserStrategy,
      EmbeddingService embeddings)
  {
    _configuration = configuration;
    _chatClient = chatClient;
    _httpClientFactory = httpClientFactory;
    _httpContextAccessor = httpContextAccessor;
    _jsonSerializerOptions = jsonSerializerOptions;
    _logger = loggerFactory.CreateLogger<MinerUAgentFactory>();
    _parserStrategy = parserStrategy;
    _embeddings = embeddings;

    _ollamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? _configuration["OLLAMA_BASE_URL"] ?? "http://localhost:11434";
    _embeddingModel = Environment.GetEnvironmentVariable("EMBEDDING_MODEL") ?? _configuration["EMBEDDING_MODEL"] ?? "bge-m3";
    _chunkEnabled = bool.TryParse(Environment.GetEnvironmentVariable("CHUNK_ENABLED"), out var ce) ? ce : true;
    _parentChunkSize = int.TryParse(Environment.GetEnvironmentVariable("CHUNK_PARENT_SIZE"), out var ps) && ps > 0 ? ps : 4096;
    _childChunkSize = int.TryParse(Environment.GetEnvironmentVariable("CHUNK_CHILD_SIZE"), out var cs) && cs > 0 ? cs : 384;

    _logger.LogInformation("Document parser strategy: {Strategy}", _parserStrategy.GetType().Name);
    _logger.LogInformation("Embedding service: {EmbeddingModel} at {OllamaUrl}", _embeddingModel, _ollamaBaseUrl);
    _logger.LogInformation("Chunking enabled: {Enabled} (parent={Parent}, child={Child})", _chunkEnabled, _parentChunkSize, _childChunkSize);
  }

  public AIAgent CreateAgent()
  {
    var compactionPipeline = new PipelineCompactionStrategy(
        new ToolResultCompactionStrategy(CompactionTriggers.TokensExceed(0x200)),
        new SlidingWindowCompactionStrategy(CompactionTriggers.TurnsExceed(4)),
        new TruncationCompactionStrategy(CompactionTriggers.TokensExceed(0x8000)));

    var innerAgent = _chatClient
        .AsBuilder()
        .UseAIContextProviders(new CompactionProvider(compactionPipeline))
        .BuildAIAgent(new ChatClientAgentOptions
        {
          Name = "MinerUAgent",
          ChatOptions = new()
          {
            Instructions = """
                        A document-processing assistant.

                        ONLY act on document uploads — do NOT call any tool unless the system message
                        explicitly says "The user has uploaded X file(s)".

                        When the system message confirms files are uploaded, follow these steps:
                        1. Call parse_documents — extract text from the files via MinerU OCR.
                        2. If parse_documents returns "No documents found" or "No content could be extracted",
                           stop immediately and tell the user the extraction failed — do NOT call search_forms or fill_form.
                        3. Analyze the extracted content. Identify ALL form types that match the document content
                           (e.g., "citizen ID card", "land use certificate", "job application", etc.).
                           Formulate a BROAD search query that covers all identified form types.
                        4. Call search_forms ONCE with that broad query — returns forms sorted by relevance.
                           - If results are empty, retry once with a different query.
                        5. For EVERY form in the search results (they are sorted by relevance, best match first):
                           - Match the extracted content to the form's fields.
                           - Call fill_form for that form with the matched values.
                           - You may call fill_form MULTIPLE times, once per matching form.
                        6. When filling values in fill_form:
                           - For "text", "number", "email", "tel", "textarea", "select", "radio", "date" fields: use the string value directly.
                           - For "checkbox" fields: use an array of selected option values, or empty array [].
                           - For "list" fields (repeating group): use an array of objects. Each object maps the sub-field IDs to their values.
                           - Use empty string "" for simple fields whose value cannot be found.
                           - Use empty array [] for list/checkbox fields whose value cannot be found.
                           - NEVER invent, guess, or fill in placeholder values.
                        """,
            Tools = [
                    AIFunctionFactory.Create(ParseDocumentsAsync, options: new() { Name = "parse_documents", SerializerOptions = _jsonSerializerOptions }),
                        AIFunctionFactory.Create(SearchFormsAsync, options: new() { Name = "search_forms", SerializerOptions = _jsonSerializerOptions }),
                        AIFunctionFactory.Create(FillFormAsync, options: new() { Name = "fill_form", SerializerOptions = _jsonSerializerOptions }),
                ],
          },
        });

    return new MinerUAgent(innerAgent, _httpContextAccessor, _logger);
  }

  // =================
  // Tools
  // =================

  [Description("Parse the uploaded documents using MinerU OCR and return the extracted text content.")]
  private async Task<string> ParseDocumentsAsync(CancellationToken cancellationToken = default)
  {
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
    var ct = linkedCts.Token;
    ct.ThrowIfCancellationRequested();

    var files = _httpContextAccessor.HttpContext?.Items["__attachments__"] as List<ExtractedFile> ?? [];
    var docFiles = files.Where(f => f.Bytes.Length > 0).ToList();

    if (docFiles.Count == 0)
      return "No documents found to parse.";

    var parsed = new List<string>();
    foreach (var file in docFiles)
    {
      var fileName = GuessFileName(file.MediaType);
      _logger.LogInformation("Parsing file with {Strategy}: {MediaType}", _parserStrategy.GetType().Name, file.MediaType);
      try
      {
        var result = await _parserStrategy.ParseAsync(file.Bytes, fileName, file.MediaType, ct);
        if (result is not null)
        {
          parsed.Add(_chunkEnabled
              ? await ChunkAndStoreAsync(fileName, file.MediaType, result, ct)
              : result);
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "MinerU parsing failed for {MediaType}", file.MediaType);
        parsed.Add($"[Parsing failed: {ex.Message}]");
      }
    }

    var text = parsed.Count > 0
        ? string.Join("\n\n---\n\n", parsed)
        : "No content could be extracted from the documents.";
    return text.Length <= 3000 ? text : text[..3000] + "\n\n[truncated]";
  }

  // ── WeKnora parent-child chunking + persistence ─────────────────────────────

  /// <summary>
  /// Chunks parsed text with WeKnora's SplitTextParentChild (parent = large section,
  /// child = embedding unit), embeds both levels with bge-m3, persists the document
  /// and its chunks, and returns the chunked text for the LLM.
  /// </summary>
  private async Task<string> ChunkAndStoreAsync(string fileName, string mediaType, string text, CancellationToken ct)
  {
    var db = _httpContextAccessor.HttpContext?.RequestServices.GetRequiredService<DbService>();

    try
    {
      var childOverlap = Math.Max(0, _childChunkSize / 5);
      var parentCfg = new WeKnora.Chunker.SplitterConfig
      {
        ChunkSize = _parentChunkSize,
        ChunkOverlap = Math.Max(0, _parentChunkSize / 5),
        Separators = ["\n\n", "\n", "。"],
      };
      var childCfg = new WeKnora.Chunker.SplitterConfig
      {
        ChunkSize = _childChunkSize,
        ChunkOverlap = childOverlap,
        Separators = ["\n\n", "\n", "。"],
      };

      var pcResult = WeKnora.Chunker.WeKnoraChunker.SplitTextParentChild(text, parentCfg, childCfg);
      _logger.LogInformation("Split {File} into {Parents} parents + {Children} child chunks",
          fileName, pcResult.Parents.Count, pcResult.Children.Count);

      if (pcResult.Children.Count == 0)
        return text;

      var parentVectors = await _embeddings.EmbedBatchAsync(
          pcResult.Parents.Select(p => p.EmbeddingContent()), ct);
      var childVectors = await _embeddings.EmbedBatchAsync(
          pcResult.Children.Select(c => c.Chunk.EmbeddingContent()), ct);

      var parentDrafts = pcResult.Parents.Select((p, i) => new ChunkDraft
      {
        Content = p.Content,
        Seq = p.Seq,
        StartAt = p.Start,
        EndAt = p.End,
        Embedding = parentVectors != null && i < parentVectors.Count ? parentVectors[i] : null,
      }).ToList();

      var childDrafts = pcResult.Children.Select((c, i) => new ChunkDraft
      {
        Content = c.Chunk.Content,
        Seq = c.Chunk.Seq,
        StartAt = c.Chunk.Start,
        EndAt = c.Chunk.End,
        ParentIndex = c.ParentIndex,
        Embedding = childVectors != null && i < childVectors.Count ? childVectors[i] : null,
      }).ToList();

      if (db is not null)
      {
        var saved = await db.CreateDocumentAsync(fileName, mediaType, text, parentDrafts, childDrafts);
        if (saved is not null)
          _logger.LogInformation("Persisted document {Id} with {P} parent + {C} child chunks",
              saved.Id, saved.ParentChunkCount, saved.ChildChunkCount);
      }

      // Return the chunked text so the LLM consumes the same content that was indexed.
      return string.Join("\n\n---\n\n", childDrafts.Select(d => d.Content));
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Chunking failed for {File} — storing raw text", fileName);
      try
      {
        if (db is not null)
          await db.CreateDocumentAsync(fileName, mediaType, text, [], []);
      }
      catch (Exception dbEx)
      {
        _logger.LogWarning(dbEx, "Failed to persist raw document {File}", fileName);
      }
      return text;
    }
  }

  [Description("Search forms by semantic similarity. Analyze the parsed document, then call this with a query describing the form type needed. Returns forms sorted by relevance.")]
  private async Task<List<FormDto>> SearchFormsAsync(
      [Description("Search query describing the type of form needed, based on the document content")] string query,
      CancellationToken cancellationToken = default)
  {
    if (_httpContextAccessor.HttpContext is { } ctx)
    {
      if (ctx.Items.ContainsKey("__forms_searched__"))
      {
        _logger.LogWarning("search_forms called more than once — returning cached result");
        return ctx.Items["__forms_cache__"] as List<FormDto> ?? [];
      }
      ctx.Items["__forms_searched__"] = true;
    }

    try
    {
      // Generate embedding for the query (Ollama) directly in the backend
      using var client = _httpClientFactory.CreateClient();
      var payload = new { model = _embeddingModel, input = query };
      using var content = new StringContent(
          JsonSerializer.Serialize(payload),
          System.Text.Encoding.UTF8,
          "application/json");

      var res = await client.PostAsync($"{_ollamaBaseUrl}/api/embed", content, cancellationToken);
      if (!res.IsSuccessStatusCode)
      {
        _logger.LogWarning("Embedding request failed: {Status}", (int)res.StatusCode);
        return [];
      }

      using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
      if (!doc.RootElement.TryGetProperty("embeddings", out var embeddings) ||
          embeddings.ValueKind != JsonValueKind.Array ||
          embeddings.GetArrayLength() == 0)
      {
        _logger.LogWarning("Embedding response has unexpected format");
        return [];
      }

      var values = new List<float>();
      foreach (var v in embeddings[0].EnumerateArray())
        values.Add(v.GetSingle());
      var queryVector = new Vector(new ReadOnlyMemory<float>(values.ToArray()));

      // Search the DB directly — no round-trip through the Next.js frontend
      var db = _httpContextAccessor.HttpContext?.RequestServices.GetRequiredService<DbService>();
      if (db is null)
      {
        _logger.LogWarning("search_forms skipped — no active HTTP context");
        return [];
      }

      var matches = await db.ListForms(queryVector);
      var forms = matches.Select(m => new FormDto
      {
        Id = m.Id,
        Title = m.Title,
        Description = m.Description,
        Fields = ParseFormFields(m.Fields),
      }).ToList();

      _logger.LogInformation("search_forms returned {Count} results for query: {Query}", forms.Count, query);
      if (_httpContextAccessor.HttpContext is { } c)
        c.Items["__forms_cache__"] = forms;
      return forms;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to search forms for query: {Query}", query);
      return [];
    }
  }

  [Description("Register a matched form fill result. Can be called MULTIPLE times if multiple forms match the document content.")]
  private Task<string> FillFormAsync(
      [Description("The ID of the matching form")] string formId,
      [Description("The display title of the matched form")] string formTitle,
      [Description("JSON object mapping each fieldId to its extracted value. Use ONLY the field \"id\" values returned by search_forms (fields[].id), never invented keys. For \"list\" fields (repeating group), the value must be an array of objects where each object maps sub-field IDs to their values; use [] if empty. For \"checkbox\" fields, use an array of strings; use [] if empty. For all other field types, use a string value. Example: {\"field_1\":\"John\", \"field_2\":[{\"sf_a\":\"Vietbank\",\"sf_b\":\"2020\"},{\"sf_a\":\"VNPT\",\"sf_b\":\"2022\"}], \"field_3\":[\"opt1\",\"opt2\"]}")] JsonElement filledValues,
      CancellationToken cancellationToken = default)
  {
    if (_httpContextAccessor.HttpContext is { } ctx)
    {
      var valueDict = filledValues.ValueKind == JsonValueKind.Object
          ? filledValues.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)
          : new Dictionary<string, JsonElement>();

      var fills = ctx.Items["__form_fills__"] as List<MinerUFormFill> ?? [];
      fills.Add(new MinerUFormFill
      {
        FormId = formId,
        FormTitle = formTitle,
        FilledValues = valueDict
      });
      ctx.Items["__form_fills__"] = fills;
      _logger.LogInformation("fill_form registered: formId={FormId} (total={Count})", formId, fills.Count);
    }
    return Task.FromResult($"Form fill registered for '{formTitle}'.");
  }

  private List<FormFieldDto>? ParseFormFields(string fieldsJson)
  {
    if (string.IsNullOrWhiteSpace(fieldsJson)) return null;
    try
    {
      return JsonSerializer.Deserialize<List<FormFieldDto>>(fieldsJson, _jsonSerializerOptions);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to parse form fields JSON");
      return null;
    }
  }

  private static string GuessFileName(string mediaType) => mediaType switch
  {
    "application/pdf" => "document.pdf",
    "image/png" => "image.png",
    "image/jpeg" or "image/jpg" => "image.jpg",
    "image/webp" => "image.webp",
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "document.docx",
    "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "document.pptx",
    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "document.xlsx",
    _ => "document"
  };
}
