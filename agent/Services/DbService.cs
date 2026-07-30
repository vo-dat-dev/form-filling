using Microsoft.EntityFrameworkCore;
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

    public async Task<List<FormInfo>> ListForms(string? searchEmbedding = null)
    {
        if (searchEmbedding != null)
        {
            var results = await db.Database.SqlQueryRaw<FormWithSimilarity>(
                """
                SELECT f.id, f.title, f.description, f.fields::text,
                       f."createdAt" AS "CreatedAt", f."updatedAt" AS "UpdatedAt",
                       f.embedding::text AS "Embedding",
                       (SELECT COUNT(*)::int FROM "FormSubmission" fs WHERE fs."formId" = f.id) AS "SubmissionCount",
                       1 - (f.embedding <=> $1::vector) AS "Similarity"
                FROM "Form" f
                WHERE f.embedding IS NOT NULL
                ORDER BY f.embedding <=> $1::vector
                LIMIT 10
                """,
                [searchEmbedding]
            ).ToListAsync();
            return results.Select(MapFormRaw).ToList();
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
                Embedding = f.Embedding,
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
                Embedding = f.Embedding,
                Count = f.Submissions.Count,
            })
            .FirstOrDefaultAsync();
        return form;
    }

    public async Task<FormInfo?> CreateForm(string title, string? description, string fields, string? embedding)
    {
        var entity = new FormEf
        {
            Title = title,
            Description = description,
            Fields = fields,
        };
        db.Forms.Add(entity);
        await db.SaveChangesAsync();

        if (embedding != null)
        {
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "Form" SET embedding = $1::vector WHERE id = $2""",
                embedding, entity.Id);
        }

        entity.Embedding = embedding;

        return new FormInfo
        {
            Id = entity.Id,
            Title = entity.Title,
            Description = entity.Description,
            Fields = entity.Fields,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Embedding = entity.Embedding,
        };
    }

    public async Task<FormInfo?> UpdateForm(string id, string title, string? description, string fields, string? newEmbedding)
    {
        var entity = await db.Forms.FindAsync(id);
        if (entity == null) return null;

        entity.Title = title;
        entity.Description = description;
        entity.Fields = fields;
        entity.UpdatedAt = DateTime.UtcNow;

        if (newEmbedding != null && newEmbedding.Length > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "Form" SET embedding = $1::vector WHERE id = $2""",
                newEmbedding, id);
        }
        else if (newEmbedding != null)
        {
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "Form" SET embedding = NULL WHERE id = $1""", id);
        }

        await db.SaveChangesAsync();

        var embedding = newEmbedding ?? await GetFormEmbedding(id);
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

    // ---- Helpers ----

    private static ThreadInfo MapThread(ThreadEf e) => new()
    {
        Id = e.Id,
        AgentId = e.AgentId,
        Title = e.Title,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    private static FormInfo MapFormRaw(FormWithSimilarity r) => new()
    {
        Id = r.Id,
        Title = r.Title,
        Description = r.Description,
        Fields = r.Fields,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        Embedding = r.Embedding,
        Count = r.SubmissionCount,
        Similarity = r.Similarity,
    };
}

// ---- Raw SQL result for vector search ----

public class FormWithSimilarity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Fields { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Embedding { get; set; }
    public int SubmissionCount { get; set; }
    public double? Similarity { get; set; }
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
    public double? Similarity { get; set; }
}

public class SubmissionInfo
{
    public string Id { get; set; } = "";
    public string FormId { get; set; } = "";
    public string Data { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public FormInfo? Form { get; set; }
}

[JsonSerializable(typeof(ThreadInfo))]
[JsonSerializable(typeof(List<ThreadInfo>))]
[JsonSerializable(typeof(FormInfo))]
[JsonSerializable(typeof(List<FormInfo>))]
[JsonSerializable(typeof(SubmissionInfo))]
[JsonSerializable(typeof(List<SubmissionInfo>))]
internal sealed partial class ApiSerializerContext : JsonSerializerContext;
