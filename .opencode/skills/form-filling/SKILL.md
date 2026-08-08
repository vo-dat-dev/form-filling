---
name: form-filling
description: How to create a form in this form-filling app. Use when the user asks to create/add a new form, build a form definition, generate a form body, or describe the form schema (fields, field types, API). Covers the FormField schema, allowed field types, the frontend->Next.js route->agent request shape, the pgvector embedding flow, a concrete example body, the EndpointGroupBase endpoint pattern, and the IEntityTypeConfiguration data-layer pattern.
---

# Form-Filling: Creating a Form

This skill is split into focused documents — read ONLY the one relevant to the
current task to keep context small.

## Quick index

| Doc | File | Use when... |
| --- | --- | --- |
| Form schema & API flow | [`form-schema.md`](form-schema.md) | Creating a form body / understanding fields & payload |
| Agent endpoints | [`endpoints.md`](endpoints.md) | Adding/fixing an API endpoint (EndpointGroupBase) |
| EF entity config | [`entity-configuration.md`](entity-configuration.md) | Adding entity config / migrations (IEntityTypeConfiguration) |

## Trigger

A form is a JSON schema of typed fields. On create/update the backend embeds
`description` with `bge-m3` (Ollama, 1024 dims) into a `vector(1024)` column.
Use the relevant sub-document above:

- **Create a form / build JSON body / field types / create/update payload** →
  read `form-schema.md`.
- **Add or fix an API endpoint** (e.g. "tạo endpoint mới", "add an endpoint",
  "fix the /api/xxx route") → read `endpoints.md`.
- **Add or modify EF entity configuration / migration** → read
  `entity-configuration.md`.

High-level flow: frontend page → Next.js route (`/api/forms`, `[id]`) →
agent (`agent/Program.cs`, port 8000). The agent DbContext is
`ApplicationDbContext` (renamed from `FormFillingDbContext`); skip that rename;
it's done.

## Sub-system overview

Agent infrastructure relevant to both endpoint and configuration work:

| Exists | Path |
| --- | --- |
| `EndpointGroupBase` | `agent/src/Web/Infrastructure/EndpointGroupBase.cs` |
| endpoint extensions | `agent/src/Web/Infrastructure/EndpointRouteBuilderExtensions.cs` |
| reflection registration | `agent/src/Web/Infrastructure/WebApplicationExtensions.cs` (+ `app.MapEndpoints()` in `Program.cs`) |
| endpoint groups | `agent/src/Web/Endpoints/{Threads,Forms,Submissions}.cs` |
| entity config classes | `agent/src/Infrastructure/Data/Configurations/*.cs` |
| DbContext + entities | `agent/Services/ApplicationDbContext.cs` |
| consumer interface | `agent/Common/Interfaces/IApplicationDbContext.cs` |

Projects uses the **global namespace** (no `namespace` blocks) and request
DTOs (`CreateFormRequest`, ...) live at the bottom of `agent/Program.cs`.