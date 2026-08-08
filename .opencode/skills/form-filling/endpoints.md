# Agent API Endpoints (EndpointGroupBase Pattern)

The agent HTTP API follows the Clean-Architecture `EndpointGroupBase` pattern
(ported from camunda-copilotkit). Endpoints are grouped per resource as plain
classes; a single `app.MapEndpoints()` call auto-registers them via reflection
— **no Program.cs changes needed when adding a new group**.

## Files

| file | role |
| --- | --- |
| `agent/src/Web/Infrastructure/EndpointGroupBase.cs` | abstract base (`GroupName` + `Map(RouteGroupBuilder)`) |
| `agent/src/Web/Infrastructure/EndpointRouteBuilderExtensions.cs` | `MapGet/MapPost/MapPut/MapPatch/MapDelete` auto-`WithName(handler.Method.Name)` |
| `agent/src/Web/Infrastructure/WebApplicationExtensions.cs` | `MapEndpoints()` reflection scan, maps `/api/{GroupName}` |
| `agent/src/Web/Endpoints/*.cs` | one class per resource (Threads, Forms, Submissions, ...) |

`agent/Program.cs` only needs the already-present line:

```csharp
app.MapEndpoints();
```

## How to add a new endpoint group

1. Create `agent/src/Web/Endpoints/<Resource>.cs`.
2. Inherit `EndpointGroupBase` and implement `Map(RouteGroupBuilder)`.
3. Register handlers using the extension methods. Pattern `""` maps to
   `/api/<Resource>`; a sub-pattern appends to it (`/{pattern}` →
   `/api/<Resource>/{pattern}`).
4. Define each handler as a method on the class; services are resolved by
   parameter injection (`DbService`, `EmbeddingService`, ...). Return strongly
   typed results with `TypedResults` / `Results`.

No registration step, no Program.cs edits, no DI changes.

## Example (`agent/src/Web/Endpoints/Threads.cs`)

```csharp
using Microsoft.AspNetCore.Http.HttpResults;

public class Threads : EndpointGroupBase
{
    public override void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(ListThreads);
        groupBuilder.MapPost(CreateThread);
        groupBuilder.MapPatch(UpdateThread, "{id}");
        groupBuilder.MapDelete(DeleteThread, "{id}");
    }

    [EndpointName(nameof(ListThreads))]
    [EndpointSummary("List conversation threads")]
    public async Task<Ok<List<ThreadInfo>>> ListThreads(DbService db, string? agentId)
        => TypedResults.Ok(await db.ListThreads(agentId ?? "formFill"));

    [EndpointName(nameof(CreateThread))]
    [EndpointSummary("Create a conversation thread")]
    public async Task<Ok<ThreadInfo>> CreateThread(DbService db, CreateThreadRequest body)
        => TypedResults.Ok(await db.CreateThread(body.AgentId ?? "formFill", body.Title ?? "New Conversation"));

    [EndpointName(nameof(UpdateThread))]
    [EndpointSummary("Update a thread")]
    public async Task<Results<Ok<ThreadInfo>, NotFound>> UpdateThread(DbService db, string id, UpdateThreadRequest body)
    {
        var thread = await db.UpdateThread(id, body.Title, body.Metadata);
        return thread != null ? TypedResults.Ok(thread) : TypedResults.NotFound();
    }

    [EndpointName(nameof(DeleteThread))]
    [EndpointSummary("Delete a thread")]
    public async Task<IResult> DeleteThread(DbService db, string id)
    {
        var ok = await db.DeleteThread(id);
        return ok ? Results.Ok(new { success = true }) : Results.NotFound();
    }
}
```

## Nested sub-path example (`agent/src/Web/Endpoints/Forms.cs`)

```csharp
public override void Map(RouteGroupBuilder groupBuilder)
{
    groupBuilder.MapGet(ListForms);
    groupBuilder.MapPost(CreateForm);
    groupBuilder.MapGet(GetForm, "{id}");
    groupBuilder.MapPut(UpdateForm, "{id}");
    groupBuilder.MapDelete(DeleteForm, "{id}");
    groupBuilder.MapGet(ListSubmissions, "{formId}/submissions");
    groupBuilder.MapPost(CreateSubmission, "{formId}/submissions");
}
```

## Gotchas

- **Global namespace**: this project uses no `namespace` blocks. Keep new
  endpoint files in the global namespace so the reflection scan and
  `Program.ParseVector` resolve without extra usings.
- **Request DTOs** (`CreateFormRequest`, `UpdateFormRequest`, ...) are declared
  once at the bottom of `agent/Program.cs` — reuse them rather than redefining.
- **Frontend contract**: the Next.js services (`src/services/*.ts`) and pages
  call exact paths like `/api/forms/{formId}/submissions` and
  `/api/submissions/{id}`. ASP.NET routing is case-insensitive, so a `Forms`
  group matches `/api/forms`. Do not rename existing groups unless you update
  the frontend too.
- **Delete responses** return `{ success = true }` (frontend checks `.ok` and
  may read `.json()`), not an empty 204.
- **Raw JSON passthrough**: submission bodies are bound as `JsonElement` and
  re-serialized with `body.GetRawText()`, because the frontend sends the
  un-wrapped object directly.