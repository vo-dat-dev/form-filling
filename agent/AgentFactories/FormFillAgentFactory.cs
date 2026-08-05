#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using Pgvector;
using System.ComponentModel;
using System.Text.Json;

public class FormFillAgentFactory : IAgentFactory
{
    public string Route => "/formFill";

    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly IChatClient _chatClient;
    private readonly ILogger _logger;
    private readonly string _ollamaBaseUrl;
    private readonly string _embeddingModel;
    private readonly IDocumentParserStrategy _parserStrategy;

    public FormFillAgentFactory(
        IConfiguration configuration,
        IChatClient chatClient,
        ILoggerFactory loggerFactory,
        IHttpContextAccessor httpContextAccessor,
        JsonSerializerOptions jsonSerializerOptions,
        IDocumentParserStrategy parserStrategy)
    {
        _configuration = configuration;
        _chatClient = chatClient;
        _httpContextAccessor = httpContextAccessor;
        _jsonSerializerOptions = jsonSerializerOptions;
        _logger = loggerFactory.CreateLogger<FormFillAgentFactory>();
        _parserStrategy = parserStrategy;

        _ollamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? _configuration["OLLAMA_BASE_URL"] ?? "http://localhost:11434";
        _embeddingModel = Environment.GetEnvironmentVariable("EMBEDDING_MODEL") ?? _configuration["EMBEDDING_MODEL"] ?? "bge-m3";

        _logger.LogInformation("Document parser strategy: {Strategy}", _parserStrategy.GetType().Name);
        _logger.LogInformation("Embedding service: {EmbeddingModel} at {OllamaUrl}", _embeddingModel, _ollamaBaseUrl);
    }

    private OllamaApiClient GetOllamaClient()
    {
        var client = new OllamaApiClient(new Uri(_ollamaBaseUrl));
        client.SelectedModel = _embeddingModel;
        return client;
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
                Name = "FormFillAgent",
                ChatOptions = new()
                {
                    Instructions = """
                        [CRITICAL] You are a document-processing and knowledge assistant.

                        [CRITICAL RULE] ALWAYS respond in Vietnamese. Every response, summary, explanation, and confirmation to the user MUST be written in Vietnamese. This is a hard requirement.

                        You have access to the following tools:
                        - parse_documents: Extract text from uploaded files using OCR
                        - search_forms: Find forms by semantic similarity
                        - search_knowledge: Search previously parsed document chunks for relevant information
                        - fill_form: Fill out a form with extracted data

                        **WORKFLOW DECISION:**
                        
                        1. **If the user asks to search for information, answer questions, or lookup knowledge:**
                           - Use search_knowledge to find relevant information from previously parsed documents
                           - DO NOT use parse_documents, search_forms, or fill_form
                           - Answer the user's question in Vietnamese based on the retrieved knowledge chunks
                        
                        2. **If the user uploads files AND requests form filling:**
                           - ONLY proceed when the system message explicitly says "The user has uploaded X file(s)"
                           - Follow the form-filling workflow below:
                           
                           a. Call parse_documents — extract text from the files.
                              The result is a LIST, one item per uploaded file. Each item has a unique
                              `documentId` and `mediaType`. It does NOT include the raw text content.
                              
                           b. If parse_documents returns an empty list, or an item whose documentId is
                              "none"/"none_N", stop immediately and tell the user the extraction failed — do NOT call search_forms or fill_form.
                              
                           c. **FOR EACH DOCUMENT** (each item has its own `documentId`):
                              - Call search_knowledge with that document's `documentId` and a query describing
                                the document type/fields to retrieve its most relevant chunks
                              - Use those chunks to identify the form type that matches THIS document
                                (e.g., "citizen ID card", "land use certificate", "job application", etc.)
                              - Formulate a specific search query for THIS document type
                              - Call search_forms with that query to find matching forms
                              - If results are empty, retry once with a different query
                               
                              d. For EVERY form found across ALL documents:
                              - Identify the form's `id` (form_id) and its fields.
                              - **STREAM THE RESULTS — SHOW INFORMATION AS YOU FIND IT, DO NOT WAIT UNTIL EVERYTHING IS DONE TO SHOW THE FORM:**
                                  - As soon as you fill each form via fill_form, immediately report in Vietnamese which document matched which form and list the filled fields and values right away.
                                  - Do NOT hold results until all documents/forms are fully processed. Show each matching form and its values as soon as that form is completed, then continue processing the rest.
                                  - Only after a form is fully filled should you summarize its outcome briefly on screen; never make the user wait for all documents before seeing any result.
                              - **FOR EACH FIELD in the matched form, VERIFY the value with an embedding search before filling it:**
                                 1. Build a targeted query from the field's `label` + `helpText` (e.g. "số CCCD, 12 chữ số" or "diện tích đất, đơn vị m2"). The helpText tells you the exact expected format, so use it to query precisely.
                                 2. Call search_knowledge with that document's `documentId` and this targeted query to retrieve the most relevant chunk(s) containing that field's value.
                                 3. Extract the value from the retrieved chunk and confirm it matches the field's helpText format (e.g. date DD/MM/YYYY, 12-digit number, allowed select value).
                                 4. Only after the value is confirmed, add it to the fill payload for this field.
                              - If a field's value cannot be confirmed from any chunk, leave it empty (do NOT guess or invent).
                              - Call fill_form for that form with the verified values.
                              - You MUST call fill_form MULTIPLE times if multiple documents/forms are uploaded
                              - Each fill_form call should use data from the CORRECT document that matches that form type
                              
                              e. **REPORT AS YOU GO (streaming) — IN VIETNAMESE:
                              - As soon as any information/field value is found or any form is filled, write it out immediately in Vietnamese — per field and per form — instead of waiting until all documents are processed.
                              - Keep each per-item report SHORT: list which document matched which form (use `mediaType` and/or `documentId`) and the key filled values.
                              - If a document matches no form, state that right away.
                              - Do NOT repeat the full form content — the form is already rendered on screen.
                              - Format example:
                              * "File pdf thứ nhất → Form CCCD/CMND: Số CCCD = 001197012345, Họ tên = Nguyễn Văn A"
                              * "File ảnh thứ hai → Form Đơn xin việc: ... (điền xong, in ngay)"
                              * "File pdf thứ ba → Không tìm thấy form phù hợp"
                           
                           f. When filling values in fill_form:
                              - Each form field returned by search_forms includes an optional "helpText" describing the expected format and a "placeholder" with an example.
                              - READ the "helpText" of every field CAREFULLY before mapping a value: it tells you the exact format required (e.g. date DD/MM/YYYY, 12-digit CCCD, no spaces/dashes, unit m2, allowed select values, etc.).
                              - Use the label + helpText to build the search_knowledge query for each field (as described in step d) so the embedding search finds the correct value.
                              - Only fill a value that was confirmed by search_knowledge and that respects the helpText rules. Never fill a value that contradicts the helpText.
                              - For "text", "number", "email", "tel", "textarea", "select", "radio", "date" fields: use the string value directly.
                              - For "checkbox" fields: use an array of selected option values, or empty array [].
                              - For "list" fields (repeating group): use an array of objects. Each object maps the sub-field IDs to their values.
                              - Use empty string "" for simple fields whose value cannot be found.
                              - Use empty array [] for list/checkbox fields whose value cannot be found.
                              - NEVER invent, guess, or fill in placeholder values.
                        
                        3. **If the user only uploads files without requesting form filling:**
                           - You may still call parse_documents to extract content
                           - Then wait for further instructions from the user

                        [CRITICAL] FINAL REMINDER: Respond ONLY in Vietnamese. Never respond in English or any other language. Vietnamese is mandatory for all user-facing text.
                        """,
                    Tools = [
                        AIFunctionFactory.Create(ParseDocumentsAsync, options: new() { Name = "parse_documents", SerializerOptions = _jsonSerializerOptions }),
                        AIFunctionFactory.Create(SearchFormsAsync, options: new() { Name = "search_forms", SerializerOptions = _jsonSerializerOptions }),
                        AIFunctionFactory.Create(SearchKnowledgeAsync, options: new() { Name = "search_knowledge", SerializerOptions = _jsonSerializerOptions }),
                        AIFunctionFactory.Create(FillFormAsync, options: new() { Name = "fill_form", SerializerOptions = _jsonSerializerOptions }),
                    ],
                },
            });

        return new FormFillAgent(innerAgent, _httpContextAccessor, _logger);
    }

    // =================
    // Tools
    // =================

    [Description("Parse the uploaded documents, chunk them, create embeddings, and store them in the knowledge base. Returns a LIST of parsed documents. Each item has a unique documentId and mediaType. To read a document's content, call search_knowledge with its documentId.")]
    private async Task<List<ParsedDocumentDto>> ParseDocumentsAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        var ct = linkedCts.Token;
        ct.ThrowIfCancellationRequested();

        var files = _httpContextAccessor.HttpContext?.Items["__attachments__"] as List<ExtractedFile> ?? [];
        var docFiles = files.Where(f => f.Bytes.Length > 0).ToList();

        if (docFiles.Count == 0)
            return new List<ParsedDocumentDto> { new() { DocumentId = "none" } };

        var parsed = new List<ParsedDocumentDto>();
        var embeddingService = _httpContextAccessor.HttpContext?.RequestServices.GetRequiredService<EmbeddingService>();
        var dbService = _httpContextAccessor.HttpContext?.RequestServices.GetRequiredService<DbService>();

        for (var idx = 0; idx < docFiles.Count; idx++)
        {
            var file = docFiles[idx];
            var docLabel = $"Document {idx + 1} ({file.MediaType})";
            _logger.LogInformation("Parsing {DocLabel} with {Strategy}", docLabel, _parserStrategy.GetType().Name);
            try
            {
                var content = await _parserStrategy.ParseAsync(file.Bytes, GuessFileName(file.MediaType), file.MediaType, ct);
                _logger.LogInformation("Extracted {Length} characters from {MediaType}", content?.Length ?? 0, file.MediaType);
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    parsed.Add(new ParsedDocumentDto { DocumentId = $"none_{idx + 1}", MediaType = file.MediaType });
                    continue;
                }

                // Chunk the content using WeKnoraChunker
                var parentCfg = new WeKnora.Chunker.SplitterConfig { ChunkSize = 1024, ChunkOverlap = 100 };
                var childCfg = new WeKnora.Chunker.SplitterConfig { ChunkSize = 512, ChunkOverlap = 80 };
                var chunkResult = WeKnora.Chunker.WeKnoraChunker.SplitTextParentChild(content, parentCfg, childCfg);

                _logger.LogInformation("Chunked into {ParentCount} parents and {ChildCount} children", 
                    chunkResult.Parents.Count, chunkResult.Children.Count);

                // Create embeddings for all chunks
                if (embeddingService != null && dbService != null)
                {
                    var parentTexts = chunkResult.Parents.Select(p => p.EmbeddingContent()).ToList();
                    var childTexts = chunkResult.Children.Select(c => c.Chunk.EmbeddingContent()).ToList();
                    var allTexts = parentTexts.Concat(childTexts).ToList();

                    var embeddings = await embeddingService.EmbedBatchAsync(allTexts, ct);
                    if (embeddings != null && embeddings.Count == allTexts.Count)
                    {
                        // Map embeddings back to chunks
                        var parentDrafts = chunkResult.Parents.Select((p, i) => new ChunkDraft
                        {
                            Content = p.Content,
                            Seq = p.Seq,
                            StartAt = p.Start,
                            EndAt = p.End,
                            ChunkType = "parent",
                            Embedding = embeddings[i],
                        }).ToList();

                        var childDrafts = chunkResult.Children.Select((c, i) => new ChunkDraft
                        {
                            Content = c.Chunk.Content,
                            Seq = c.Chunk.Seq,
                            StartAt = c.Chunk.Start,
                            EndAt = c.Chunk.End,
                            ChunkType = "child",
                            Embedding = embeddings[parentTexts.Count + i],
                            ParentIndex = c.ParentIndex,
                        }).ToList();

                        // Store document and chunks in database
                        var docInfo = await dbService.CreateDocumentAsync(
                            GuessFileName(file.MediaType),
                            file.MediaType,
                            content,
                            parentDrafts,
                            childDrafts);

                        parsed.Add(new ParsedDocumentDto
                        {
                            DocumentId = docInfo?.Id ?? $"no_id_{idx + 1}",
                            MediaType = file.MediaType,
                        });

                        _logger.LogInformation("Stored document {DocId} with {ParentCount} parents and {ChildCount} children",
                            docInfo?.Id, parentDrafts.Count, childDrafts.Count);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to create embeddings for chunks");
                        parsed.Add(new ParsedDocumentDto
                        {
                            DocumentId = $"none_{idx + 1}",
                            MediaType = file.MediaType,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Parsing failed for {DocLabel}", docLabel);
                parsed.Add(new ParsedDocumentDto { DocumentId = $"none_{idx + 1}", MediaType = file.MediaType });
            }
        }

        return parsed.Count > 0 ? parsed : new List<ParsedDocumentDto> { new() { DocumentId = "none" } };
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
            // Generate embedding for the query using OllamaSharp
            var ollama = GetOllamaClient();
            var request = new EmbedRequest
            {
                Model = _embeddingModel,
                Input = [query]
            };

            var response = await ollama.EmbedAsync(request, cancellationToken);
            if (response?.Embeddings == null || response.Embeddings.Count == 0)
            {
                _logger.LogWarning("Embedding response is empty for query: {Query}", query);
                return [];
            }

            var embedding = response.Embeddings[0];
            var queryVector = new Vector(new ReadOnlyMemory<float>(embedding));

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

    [Description("Search knowledge base by semantic similarity. Pass a documentId (from parse_documents) to scope the search to just that document; pass a query relevant to the information needed.")]
    private async Task<List<KnowledgeChunkDto>> SearchKnowledgeAsync(
        [Description("Search query to find relevant knowledge from parsed documents")] string query,
        [Description("Optional documentId (from parse_documents) to limit search to a single document. Pass the documentId you need the content from.")] string? documentId = null,
        [Description("Maximum number of results to return (default: 5)")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate embedding for the query using OllamaSharp
            var ollama = GetOllamaClient();
            var request = new EmbedRequest
            {
                Model = _embeddingModel,
                Input = [query]
            };

            var response = await ollama.EmbedAsync(request, cancellationToken);
            if (response?.Embeddings == null || response.Embeddings.Count == 0)
            {
                _logger.LogWarning("Embedding response is empty for knowledge search: {Query}", query);
                return [];
            }

            var embedding = response.Embeddings[0];
            var queryVector = new Vector(new ReadOnlyMemory<float>(embedding));

            // Search the knowledge base
            var db = _httpContextAccessor.HttpContext?.RequestServices.GetRequiredService<DbService>();
            if (db is null)
            {
                _logger.LogWarning("SearchKnowledge skipped — no active HTTP context");
                return [];
            }

            var results = await db.SearchKnowledge(queryVector, limit, documentId);
            var chunks = results.Select(r => new KnowledgeChunkDto
            {
                ChunkId = r.ChunkId,
                DocumentId = r.DocumentId,
                Content = r.Content,
                ChunkType = r.ChunkType,
                Distance = r.Distance,
                FileName = r.FileName,
            }).ToList();

            _logger.LogInformation("SearchKnowledge returned {Count} results for query: {Query}", chunks.Count, query);
            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search knowledge for query: {Query}", query);
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

            var fills = ctx.Items["__form_fills__"] as List<FormFillResult> ?? [];
            fills.Add(new FormFillResult
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
