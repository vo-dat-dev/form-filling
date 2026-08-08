using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Pgvector;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<ThreadEf> Threads => Set<ThreadEf>();
    public DbSet<FormEf> Forms => Set<FormEf>();
    public DbSet<FormSubmissionEf> FormSubmissions => Set<FormSubmissionEf>();
    public DbSet<DocumentEf> Documents => Set<DocumentEf>();
    public DbSet<DocumentChunkEf> DocumentChunks => Set<DocumentChunkEf>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}

public class ThreadEf
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AgentId { get; set; } = "";
    public string Title { get; set; } = "New Conversation";
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class FormEf
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Fields { get; set; } = "[]";
    public Vector? Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<FormSubmissionEf> Submissions { get; set; } = [];
}

public class FormSubmissionEf
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FormId { get; set; } = "";
    public string Data { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public FormEf? Form { get; set; }
}

public class DocumentEf
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FileName { get; set; } = "";
    public string? MediaType { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<DocumentChunkEf> Chunks { get; set; } = [];
}

public class DocumentChunkEf
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DocumentId { get; set; } = "";
    public string Content { get; set; } = "";
    public int Seq { get; set; }
    public int StartAt { get; set; }
    public int EndAt { get; set; }
    public string? ParentChunkId { get; set; }
    public string ChunkType { get; set; } = "text";
    public Vector? Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DocumentEf? Document { get; set; }
    public DocumentChunkEf? Parent { get; set; }
}