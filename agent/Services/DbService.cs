using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using System.Text.Json.Serialization;

public class DbService(FormFillingDbContext db)
{
    // ---- Threads ----

    public async Task<List<ThreadInfo>> ListThreads(string agentId)
    {
        var threads = await db.Threads
            .Where(t => t.AgentId == agentId)
            .OrderByDescending(t => t.UpdatedAt)
            .Select(t => new ThreadInfo
            {
                Id = t.Id,
                AgentId = t.AgentId,
                Title = t.Title,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
            })
            .ToListAsync();
        return threads;
    }

    public async Task<ThreadInfo> CreateThread(string agentId, string title)
    {
        var entity = new ThreadEf { AgentId = agentId, Title = title };
        db.Threads.Add(entity);
        await db.SaveChangesAsync();
        return new ThreadInfo
        {
            Id = entity.Id,
            AgentId = entity.AgentId,
            Title = entity.Title,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }

    public async Task<ThreadInfo?> UpdateThread(string id, string? title, string? metadata)
    {
        var entity = await db.Threads.FindAsync(id);
        if (entity == null) return null;
        if (title != null) entity.Title = title;
        if (metadata != null) entity.Metadata = metadata;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return MapThread(entity);
    }

    public async Task<ThreadInfo?> GetThread(string id)
    {
        var entity = await db.Threads.FindAsync(id);
        return entity == null ? null : MapThread(entity);
    }

    public async Task<bool> DeleteThread(string id)
    {
        var entity = await db.Threads.FindAsync(id);
        if (entity == null) return false;
        db.Threads.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    // ---- Forms ----

    public async Task<List<FormInfo>> ListForms(Vector? searchVector = null)
    {
        if (searchVector != null)
        {
            return await db.Forms
                .Where(f => f.Embedding != null)
                .Select(f => new
                {
                    Form = f,
                    Distance = f.Embedding!.CosineDistance(searchVector),
                    Count = f.Submissions.Count,
                })
                .OrderBy(x => x.Distance)
                .Take(10)
                .Select(x => new FormInfo
                {
                    Id = x.Form.Id,
                    Title = x.Form.Title,
                    Description = x.Form.Description,
                    Fields = x.Form.Fields,
                    CreatedAt = x.Form.CreatedAt,
                    UpdatedAt = x.Form.UpdatedAt,
                    Embedding = x.Form.Embedding != null ? x.Form.Embedding.ToString() : null,
                    Count = x.Count,
                })
                .ToListAsync();
        }

        var all = await db.Forms
            .OrderByDescending(f => f.UpdatedAt)
            .Select(f => new FormInfo
            {
                Id = f.Id,
                Title = f.Title,
                Description = f.Description,
                Fields = f.Fields,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
                Count = f.Submissions.Count,
            })
            .ToListAsync();
        return all;
    }

    public async Task<FormInfo?> GetForm(string id)
    {
        var form = await db.Forms
            .Where(f => f.Id == id)
            .Select(f => new FormInfo
            {
                Id = f.Id,
                Title = f.Title,
                Description = f.Description,
                Fields = f.Fields,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
                Embedding = f.Embedding != null ? f.Embedding.ToString() : null,
                Count = f.Submissions.Count,
            })
            .FirstOrDefaultAsync();
        return form;
    }

    public async Task<FormInfo?> CreateForm(string title, string? description, string fields, Vector? embedding)
    {
        var entity = new FormEf
        {
            Title = title,
            Description = description,
            Fields = fields,
            Embedding = embedding,
        };
        db.Forms.Add(entity);
        await db.SaveChangesAsync();

        return new FormInfo
        {
            Id = entity.Id,
            Title = entity.Title,
            Description = entity.Description,
            Fields = entity.Fields,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Embedding = entity.Embedding?.ToString(),
        };
    }

    public async Task<FormInfo?> UpdateForm(string id, string title, string? description, string fields, Vector? newEmbedding, bool descriptionChanged)
    {
        var entity = await db.Forms.FindAsync(id);
        if (entity == null) return null;

        entity.Title = title;
        entity.Description = description;
        entity.Fields = fields;
        entity.UpdatedAt = DateTime.UtcNow;

        if (descriptionChanged)
            entity.Embedding = newEmbedding;

        await db.SaveChangesAsync();

        var embedding = descriptionChanged ? newEmbedding?.ToString() : await GetFormEmbedding(id);
        return new FormInfo
        {
            Id = entity.Id,
            Title = entity.Title,
            Description = entity.Description,
            Fields = entity.Fields,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Embedding = embedding,
        };
    }

    public async Task<bool> DeleteForm(string id)
    {
        var entity = await db.Forms.FindAsync(id);
        if (entity == null) return false;
        db.Forms.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<string?> GetFormEmbedding(string id)
    {
        var result = await db.Database.SqlQueryRaw<string>(
            """SELECT embedding::text FROM "Form" WHERE id = $1""",
            [id]
        ).FirstOrDefaultAsync();
        return result;
    }

    // ---- Submissions ----

    public async Task<List<SubmissionInfo>> ListSubmissions(string formId)
    {
        var submissions = await db.FormSubmissions
            .Where(s => s.FormId == formId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SubmissionInfo
            {
                Id = s.Id,
                FormId = s.FormId,
                Data = s.Data,
                CreatedAt = s.CreatedAt,
            })
            .ToListAsync();
        return submissions;
    }

    public async Task<SubmissionInfo?> CreateSubmission(string formId, string data)
    {
        var formExists = await db.Forms.AnyAsync(f => f.Id == formId);
        if (!formExists) return null;

        var entity = new FormSubmissionEf { FormId = formId, Data = data };
        db.FormSubmissions.Add(entity);
        await db.SaveChangesAsync();
        return new SubmissionInfo
        {
            Id = entity.Id,
            FormId = entity.FormId,
            Data = entity.Data,
            CreatedAt = entity.CreatedAt,
        };
    }

    public async Task<SubmissionInfo?> GetSubmission(string id)
    {
        var submission = await db.FormSubmissions
            .Where(s => s.Id == id)
            .Select(s => new SubmissionInfo
            {
                Id = s.Id,
                FormId = s.FormId,
                Data = s.Data,
                CreatedAt = s.CreatedAt,
                Form = s.Form != null ? new FormInfo
                {
                    Id = s.Form.Id,
                    Title = s.Form.Title,
                    Description = s.Form.Description,
                    Fields = s.Form.Fields,
                } : null,
            })
            .FirstOrDefaultAsync();
        return submission;
    }

    // ---- Knowledge Search ----

    /// <summary>
    /// Search document chunks by semantic similarity using vector embedding.
    /// Returns the most relevant chunks ordered by cosine distance.
    /// </summary>
    public async Task<List<KnowledgeChunkInfo>> SearchKnowledge(Vector queryVector, int limit = 5, string? documentId = null)
    {
        var query = db.DocumentChunks
            .Where(c => c.Embedding != null);

        if (!string.IsNullOrWhiteSpace(documentId))
            query = query.Where(c => c.DocumentId == documentId);

        var results = await query
            .Select(c => new
            {
                Chunk = c,
                Distance = c.Embedding!.CosineDistance(queryVector),
            })
            .OrderBy(x => x.Distance)
            .Take(limit)
            .Select(x => new KnowledgeChunkInfo
            {
                ChunkId = x.Chunk.Id,
                DocumentId = x.Chunk.DocumentId,
                Content = x.Chunk.Content,
                ChunkType = x.Chunk.ChunkType,
                Distance = x.Distance,
                FileName = x.Chunk.Document != null ? x.Chunk.Document.FileName : null,
            })
            .ToListAsync();
        return results;
    }

    // ---- Documents & chunks ----

    /// <summary>
    /// Returns the most recently created documents (used to list parsed docs).
    /// </summary>
    public async Task<List<DocumentInfo>> ListDocuments(int limit = 20)
    {
        return await db.Documents
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .Select(d => new DocumentInfo
            {
                Id = d.Id,
                FileName = d.FileName,
                MediaType = d.MediaType,
                Content = d.Content,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                ParentChunkCount = d.Chunks.Count(c => c.ChunkType == "parent"),
                ChildChunkCount = d.Chunks.Count(c => c.ChunkType == "child"),
            })
            .ToListAsync();
    }

    /// <summary>
    /// Returns a single document by id with its full content.
    /// </summary>
    public async Task<DocumentInfo?> GetDocument(string id)
    {
        var d = await db.Documents
            .Where(x => x.Id == id)
            .Select(d => new DocumentInfo
            {
                Id = d.Id,
                FileName = d.FileName,
                MediaType = d.MediaType,
                Content = d.Content,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                ParentChunkCount = d.Chunks.Count(c => c.ChunkType == "parent"),
                ChildChunkCount = d.Chunks.Count(c => c.ChunkType == "child"),
            })
            .FirstOrDefaultAsync();
        return d;
    }

    /// <summary>
    /// Persists a parsed document plus its parent/child chunks.
    /// parents[i] and children[j].ParentIndex maps children to parents (index into
    /// parents, -1 when the child needs no parent).
    /// </summary>
    public async Task<DocumentInfo?> CreateDocumentAsync(
        string fileName, string? mediaType, string content,
        List<ChunkDraft> parents, List<ChunkDraft> children)
    {
        var doc = new DocumentEf { FileName = fileName, MediaType = mediaType, Content = content };

        var parentEntities = parents.Select((p, i) => new DocumentChunkEf
        {
            DocumentId = doc.Id,
            Content = p.Content,
            Seq = p.Seq,
            StartAt = p.StartAt,
            EndAt = p.EndAt,
            ChunkType = p.ChunkType,
            Embedding = p.Embedding,
        }).ToList();

        var childEntities = new List<DocumentChunkEf>(children.Count);
        for (var i = 0; i < children.Count; i++)
        {
            var c = children[i];
            var parentChunkId = c.ParentIndex >= 0 && c.ParentIndex < parentEntities.Count
                ? parentEntities[c.ParentIndex].Id
                : null;
            childEntities.Add(new DocumentChunkEf
            {
                DocumentId = doc.Id,
                Content = c.Content,
                Seq = c.Seq,
                StartAt = c.StartAt,
                EndAt = c.EndAt,
                ChunkType = c.ChunkType,
                ParentChunkId = parentChunkId,
                Embedding = c.Embedding,
            });
        }

        doc.Chunks.AddRange(parentEntities);
        doc.Chunks.AddRange(childEntities);
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        return new DocumentInfo
        {
            Id = doc.Id,
            FileName = doc.FileName,
            MediaType = doc.MediaType,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
            ParentChunkCount = parentEntities.Count,
            ChildChunkCount = childEntities.Count,
        };
    }

    // ---- Helpers ----

    private static ThreadInfo MapThread(ThreadEf e) => new()
    {
        Id = e.Id,
        AgentId = e.AgentId,
        Title = e.Title,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };
}

// ---- DTOs ----

public class ThreadInfo
{
    public string Id { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class FormInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Fields { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Embedding { get; set; }
    public int Count { get; set; }
}

public class SubmissionInfo
{
    public string Id { get; set; } = "";
    public string FormId { get; set; } = "";
    public string Data { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public FormInfo? Form { get; set; }
}

public class DocumentInfo
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? MediaType { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int ParentChunkCount { get; set; }
    public int ChildChunkCount { get; set; }
}

/// <summary>Chunk data awaiting persistence (parents and children use the same shape).</summary>
public class ChunkDraft
{
    public string Content { get; set; } = "";
    public int Seq { get; set; }
    public int StartAt { get; set; }
    public int EndAt { get; set; }
    public string ChunkType { get; set; } = "text";
    public Vector? Embedding { get; set; }
    /// <summary>Index into the parents list; -1 when no parent applies.</summary>
    public int ParentIndex { get; set; } = -1;
}

public class KnowledgeChunkInfo
{
    public string ChunkId { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public string Content { get; set; } = "";
    public string ChunkType { get; set; } = "text";
    public double Distance { get; set; }
    public string? FileName { get; set; }
}

[JsonSerializable(typeof(ThreadInfo))]
[JsonSerializable(typeof(List<ThreadInfo>))]
[JsonSerializable(typeof(FormInfo))]
[JsonSerializable(typeof(List<FormInfo>))]
[JsonSerializable(typeof(SubmissionInfo))]
[JsonSerializable(typeof(List<SubmissionInfo>))]
[JsonSerializable(typeof(DocumentInfo))]
[JsonSerializable(typeof(List<DocumentInfo>))]
[JsonSerializable(typeof(ChunkDraft))]
[JsonSerializable(typeof(List<ChunkDraft>))]
[JsonSerializable(typeof(KnowledgeChunkInfo))]
[JsonSerializable(typeof(List<KnowledgeChunkInfo>))]
internal sealed partial class ApiSerializerContext : JsonSerializerContext;
