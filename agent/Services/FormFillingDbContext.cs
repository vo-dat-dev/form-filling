using Microsoft.EntityFrameworkCore;

public class FormFillingDbContext : DbContext
{
    public FormFillingDbContext(DbContextOptions<FormFillingDbContext> options) : base(options) { }

    public DbSet<ThreadEf> Threads => Set<ThreadEf>();
    public DbSet<FormEf> Forms => Set<FormEf>();
    public DbSet<FormSubmissionEf> FormSubmissions => Set<FormSubmissionEf>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    public string? Embedding { get; set; }
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
