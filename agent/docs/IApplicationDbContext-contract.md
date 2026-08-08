# IApplicationDbContext — Contract (Clean Architecture)

> Reference: [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) — article 14 "IApplicationDbContext Contract".
> This document describes the contract and how it applies to the `agent/FormFilling` project.

## 1. Why an abstraction at all?

`IApplicationDbContext` is the single point of contact between the Application layer and the persistence mechanism. It is a deliberately lean abstraction — exposing domain entity `DbSet<T>` properties and a single `SaveChangesAsync` method — that lets consumers read and write data without any knowledge of EF Core providers, connection strings, Identity tables, or interceptors.

This follows the **dependency rule** at the heart of Clean Architecture: *inner layers must not reference outer layers*.

- The interface is declared **inside** (Application) and lives there permanently.
- The concrete `ApplicationDbContext` — which knows about providers, Identity, interceptors — lives **outside** (Infrastructure).
- The dependency arrow points inward, satisfying dependency inversion.

## 2. The interface contract

The interface contains exactly the domain aggregate `DbSet<T>` properties and a `SaveChangesAsync` method mirroring the signature consumers need:

```csharp
public interface IApplicationDbContext
{
    DbSet<TodoList> TodoLists { get; }
    DbSet<TodoItem> TodoItems { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
```

**Applied to this project** — the `FormFilling` agent has 5 aggregates/entities:

```csharp
public interface IFormFillingDbContext
{
    DbSet<ThreadEf> Threads { get; }
    DbSet<FormEf> Forms { get; }
    DbSet<FormSubmissionEf> FormSubmissions { get; }
    DbSet<DocumentEf> Documents { get; }
    DbSet<DocumentChunkEf> DocumentChunks { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

No query-specific members are exposed. Queries leverage the fact that `DbSet<T>` implements `IQueryable<T>`, so `ProjectTo<T>()`, `AsNoTracking()`, `OrderBy`, and `ToListAsync` are all available without any additional surface area on the interface. The contract stays honest: it describes *what data exists*, not *how it is queried*.

## 3. Concrete implementation

In the Infrastructure layer, `ApplicationDbContext` implements `IApplicationDbContext`. The `DbSet` properties delegate to `Set<T>()` from the base `DbContext`, and `OnModelCreating` applies all EF Core entity configurations:

```csharp
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    public DbSet<TodoList> TodoLists => Set<TodoList>();
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();
    protected override void OnModelCreating(ModelBuilder builder) { ... }
}
```

**Applied to this project:**

```csharp
public class FormFillingDbContext : DbContext, IFormFillingDbContext
{
    public FormFillingDbContext(DbContextOptions<FormFillingDbContext> options) : base(options) { }

    public DbSet<ThreadEf> Threads => Set<ThreadEf>();
    public DbSet<FormEf> Forms => Set<FormEf>();
    public DbSet<FormSubmissionEf> FormSubmissions => Set<FormSubmissionEf>();
    public DbSet<DocumentEf> Documents => Set<DocumentEf>();
    public DbSet<DocumentChunkEf> DocumentChunks => Set<DocumentChunkEf>();
}
```

> Note: this project uses plain `DbContext` (no Identity), so the `IdentityDbContext` inheritance does not apply.

## 4. Dependency flow

```
┌─────────────────────────┐
│  Web / API (Program.cs) │
│  ── endpoints ──────────│
└───────────┬─────────────┘
            │ inject
┌───────────▼─────────────┐
│  Application (consumers)│
│  DbService, AgentTools  │
│  → know the interface   │
└───────────┬─────────────┘
            │ implement
┌───────────▼─────────────┐
│  Infrastructure (Data)  │
│  FormFillingDbContext   │
└─────────────────────────┘
```

- **Consumers** (Application): `DbService`, `FormFillAgentFactory` — depend solely on `IFormFillingDbContext`.
- **Implementation** (Infrastructure): `FormFillingDbContext` — knows about Npgsql, Pgvector, connection string.

## 5. DI registration and service lifetime

The interface ↔ implementation connection is made in one place (composition root). `AddDbContext` defaults to **Scoped** lifetime, then the interface is re-registered using a factory delegate:

```csharp
builder.Services.AddScoped<IApplicationDbContext>(
    provider => provider.GetRequiredService<ApplicationDbContext>());
```

**Applied to this project** (`Program.cs`):

```csharp
builder.Services.AddDbContext<FormFillingDbContext>(options =>
    options.UseNpgsql(connString, npgsql => npgsql.UseVector()));

builder.Services.AddScoped<IFormFillingDbContext>(
    provider => provider.GetRequiredService<FormFillingDbContext>());

builder.Services.AddScoped<DbService>();
```

- **Scoped** lifetime: each HTTP request gets its own `DbContext` instance — standard EF Core practice that ensures unit-of-work semantics and prevents entity tracking conflicts across concurrent requests.
- The factory delegate pattern (rather than `AddDbContext<T, I>()`) is used because `FormFillingDbContext` needs its own provider configuration (Npgsql + Vector): the implementation is configured via `AddDbContext`, and the interface is just an alias.

## 6. Consumer patterns

Every handler follows the same pattern: a constructor receives `IApplicationDbContext` and uses it directly. No repository wrappers, no unit-of-work beyond `SaveChangesAsync`.

| Consumer | Operation | Interface usage |
|----------|-----------|-----------------|
| `DbService.CreateThread/CreateForm/CreateSubmission` | Write | `db.Threads.Add(entity)` → `SaveChangesAsync` |
| `DbService.Update*/Delete*` | Read + Update/Delete | `FindAsync` → mutate/`Remove` → `SaveChangesAsync` |
| `DbService.ListThreads/ListForms/ListSubmissions` | Read-only | LINQ + `ToListAsync` (projection) |
| `DbService.SearchKnowledge` | Read-only | `AsNoTracking` + `CosineDistance` |
| `FormFillAgentFactory` (tools) | Read-only | via `DbService` |

Read-only queries should use `AsNoTracking()` to avoid change tracking overhead. Projection is pushed down into SQL, selecting only the needed columns.

## 7. What the interface intentionally hides

- **Database provider selection**: `#if UsePostgreSQL / UseSqlServer / SQLite` blocks live entirely in Infrastructure's DI. Application references only `Microsoft.EntityFrameworkCore` (the abstractions).
- **EF Core interceptors**: if `AuditableEntityInterceptor` (sets `Created`/`LastModified`) or `DispatchDomainEventsInterceptor` exist, they are wired into the `DbContextOptions` pipeline inside Infrastructure. Handlers call `SaveChangesAsync` without knowing auditing/event dispatch happens automatically as a cross-cutting concern.
- **Identity tables**: `AspNetUsers`, `AspNetRoles`, etc. are managed by `IdentityDbContext` — invisible through the interface.

## 8. Adding a new aggregate (e.g. entity `Project`)

When adding a new aggregate root, you **must** add its `DbSet<T>` property to **both**:

1. `IFormFillingDbContext` (Application layer) — so consumers can use it.
2. `FormFillingDbContext` (Infrastructure layer) — so EF Core registers the entity set with its model.

The application will compile with only the interface member, but **all runtime queries will fail** because the concrete context never registered the entity set.

## 9. Testing approach

The template's testing strategy: test against a real database (no fakes). A real database provider catches provider-specific query behaviour, index violations, transaction semantics, and constraint failures that in-memory providers and mocked interfaces would silently pass. The interface registration is **not** overridden in tests — the real `FormFillingDbContext` runs unchanged.
