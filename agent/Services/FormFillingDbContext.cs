using Microsoft.EntityFrameworkCore;
using Pgvector;

public class FormFillingDbContext : DbContext
{
    public FormFillingDbContext(DbContextOptions<FormFillingDbContext> options) : base(options) { }

    public DbSet<ThreadEf> Threads => Set<ThreadEf>();
    public DbSet<FormEf> Forms => Set<FormEf>();
    public DbSet<FormSubmissionEf> FormSubmissions => Set<FormSubmissionEf>();
    public DbSet<DocumentEf> Documents => Set<DocumentEf>();
    public DbSet<DocumentChunkEf> DocumentChunks => Set<DocumentChunkEf>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<ThreadEf>(e =>
        {
            e.ToTable("Thread");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AgentId).HasColumnName("agentId").IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasDefaultValue("New Conversation");
            e.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("createdAt");
            e.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
            e.HasIndex(x => x.AgentId);
        });

        modelBuilder.Entity<FormEf>(e =>
        {
            e.ToTable("Form");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Title).HasColumnName("title").IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Fields).HasColumnName("fields").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1024)");
            e.Property(x => x.CreatedAt).HasColumnName("createdAt");
            e.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
        });

        modelBuilder.Entity<FormSubmissionEf>(e =>
        {
            e.ToTable("FormSubmission");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FormId).HasColumnName("formId").IsRequired();
            e.Property(x => x.Data).HasColumnName("data").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("createdAt");
            e.HasOne(x => x.Form)
                .WithMany(f => f.Submissions)
                .HasForeignKey(x => x.FormId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentEf>(e =>
        {
            e.ToTable("Document");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FileName).HasColumnName("fileName").IsRequired();
            e.Property(x => x.MediaType).HasColumnName("mediaType");
            e.Property(x => x.Content).HasColumnName("content").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("createdAt");
            e.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
            e.HasMany(x => x.Chunks)
                .WithOne(c => c.Document)
                .HasForeignKey(c => c.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunkEf>(e =>
        {
            e.ToTable("Chunk");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DocumentId).HasColumnName("documentId").IsRequired();
            e.Property(x => x.Content).HasColumnName("content").IsRequired();
            e.Property(x => x.Seq).HasColumnName("chunk_index");
            e.Property(x => x.StartAt).HasColumnName("start_at");
            e.Property(x => x.EndAt).HasColumnName("end_at");
            e.Property(x => x.ParentChunkId).HasColumnName("parent_chunk_id");
            e.Property(x => x.ChunkType).HasColumnName("chunk_type").HasDefaultValue("text");
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1024)");
            e.Property(x => x.CreatedAt).HasColumnName("createdAt");
            e.HasIndex(x => x.DocumentId);
            e.HasIndex(x => x.ParentChunkId);
            e.HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentChunkId)
                .OnDelete(DeleteBehavior.Restrict);
        });
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
