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