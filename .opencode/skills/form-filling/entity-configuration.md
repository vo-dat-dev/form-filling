# EF Core Entity Configuration (IEntityTypeConfiguration Pattern)

Entity mapping is NOT done inline in `OnModelCreating`. Each entity gets its
own `IEntityTypeConfiguration<TEntity>` class under
`agent/src/Infrastructure/Data/Configurations/`, and the DbContext applies
them all in one line. Matches the camunda-copilotkit repo layout.

## Files

| file | role |
| --- | --- |
| `agent/src/Infrastructure/Data/Configurations/*.cs` | one configuration class per entity |
| `agent/Services/ApplicationDbContext.cs` | DbContext + entity POCOs + `ApplyConfigurationsFromAssembly` |
| `agent/Common/Interfaces/IApplicationDbContext.cs` | consumer interface (`DbSet`s + `SaveChangesAsync`) |

`ApplicationDbContext.OnModelCreating` is just:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.HasPostgresExtension("vector");
    modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
}
```

## How to add configuration for a new entity

1. Add the entity POCO class (e.g. `MyEntityEf`) to
   `agent/Services/ApplicationDbContext.cs`.
2. Create `agent/src/Infrastructure/Data/Configurations/MyEntityConfiguration.cs`
   implementing `IEntityTypeConfiguration<MyEntityEf>`.
3. Register a `DbSet<MyEntityEf>` in `ApplicationDbContext` and, if consumers
   need it, in `IApplicationDbContext`.
4. No manual registration of the configuration — the assembly scan picks it up.
5. Create an EF migration:
   `dotnet ef migrations add <Name> --project agent/FormFilling.csproj`
   then verify `dotnet ef migrations has-pending-model-changes --project agent/FormFilling.csproj`
   must report "No changes".

## Example (`agent/src/Infrastructure/Data/Configurations/ThreadConfiguration.cs`)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ThreadConfiguration : IEntityTypeConfiguration<ThreadEf>
{
    public void Configure(EntityTypeBuilder<ThreadEf> builder)
    {
        builder.ToTable("Thread");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.AgentId).HasColumnName("agentId").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasDefaultValue("New Conversation");
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
        builder.HasIndex(x => x.AgentId);
    }
}
```

## Example with relationships + pgvector (`FormSubmissionConfiguration.cs`)

```csharp
public class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmissionEf>
{
    public void Configure(EntityTypeBuilder<FormSubmissionEf> builder)
    {
        builder.ToTable("FormSubmission");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.FormId).HasColumnName("formId").IsRequired();
        builder.Property(x => x.Data).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.HasOne(x => x.Form)
            .WithMany(f => f.Submissions)
            .HasForeignKey(x => x.FormId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- Vector columns use `.HasColumnType("vector(1024)")` (the `bge-m3` embedding
  dimension).
- A self-referencing chunk uses `.HasOne(x => x.Parent)` +
  `OnDelete(DeleteBehavior.Restrict)`.

## Gotchas

- **Global namespace**: entity configuration classes live in the global
  namespace (no `namespace` block), like the rest of the project.
- **Entity POCOs stay in `ApplicationDbContext.cs`** for now; the
  `src/Domain/Entities/` folder is scaffolding and not wired into the project.
  Keep `using Pgvector;` in the DbContext file for `Vector` properties.
- `HasPostgresExtension("vector")` must be declared before `vector(...)` columns.
- After changing any configuration, run `has-pending-model-changes`. If the DB
  is already deployed and changes are detected, create a NEW migration rather
  than editing old ones.
- **Naming history**: the DbContext was renamed `FormFillingDbContext` →
  `ApplicationDbContext` (and `IFormFillingDbContext` → `IApplicationDbContext`).
  Migration files and the snapshot reference
  `[DbContext(typeof(ApplicationDbContext))]`. Don't reintroduce old names.