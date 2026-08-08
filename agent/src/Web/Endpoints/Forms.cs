using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json;

public class Forms : EndpointGroupBase
{
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

    [EndpointName(nameof(ListForms))]
    [EndpointSummary("List forms, optionally semantic search")]
    public async Task<Ok<List<FormInfo>>> ListForms(DbService db, string? q)
    {
        var forms = string.IsNullOrWhiteSpace(q)
            ? await db.ListForms()
            : await db.ListForms(Program.ParseVector(q));
        return TypedResults.Ok(forms);
    }

    [EndpointName(nameof(CreateForm))]
    [EndpointSummary("Create a form")]
    public async Task<Ok<FormInfo>> CreateForm(DbService db, EmbeddingService embeddings, CreateFormRequest body)
    {
        var embedding = await embeddings.EmbedAsync(body.Description);
        var form = await db.CreateForm(body.Title, body.Description, body.Fields, embedding);
        return TypedResults.Ok(form!);
    }

    [EndpointName(nameof(GetForm))]
    [EndpointSummary("Get a form by id")]
    public async Task<Results<Ok<FormInfo>, NotFound>> GetForm(DbService db, string id)
    {
        var form = await db.GetForm(id);
        return form != null ? TypedResults.Ok(form) : TypedResults.NotFound();
    }

    [EndpointName(nameof(UpdateForm))]
    [EndpointSummary("Update a form")]
    public async Task<Results<Ok<FormInfo>, NotFound>> UpdateForm(DbService db, EmbeddingService embeddings, string id, UpdateFormRequest body)
    {
        var existing = await db.GetForm(id);
        if (existing == null) return TypedResults.NotFound();

        var descriptionChanged = body.Description != existing.Description;
        var newEmbedding = descriptionChanged
            ? await embeddings.EmbedAsync(body.Description)
            : null;

        var form = await db.UpdateForm(id, body.Title, body.Description, body.Fields, newEmbedding, descriptionChanged);
        return form != null ? TypedResults.Ok(form) : TypedResults.NotFound();
    }

    [EndpointName(nameof(DeleteForm))]
    [EndpointSummary("Delete a form")]
    public async Task<IResult> DeleteForm(DbService db, string id)
    {
        var ok = await db.DeleteForm(id);
        return ok ? Results.Ok(new { success = true }) : Results.NotFound();
    }

    [EndpointName(nameof(ListSubmissions))]
    [EndpointSummary("List submissions of a form")]
    public async Task<Ok<List<SubmissionInfo>>> ListSubmissions(DbService db, string formId)
        => TypedResults.Ok(await db.ListSubmissions(formId));

    [EndpointName(nameof(CreateSubmission))]
    [EndpointSummary("Submit data for a form")]
    public async Task<Results<Ok<SubmissionInfo>, NotFound>> CreateSubmission(DbService db, string formId, JsonElement body)
    {
        var submission = await db.CreateSubmission(formId, body.GetRawText());
        return submission != null ? TypedResults.Ok(submission) : TypedResults.NotFound();
    }
}