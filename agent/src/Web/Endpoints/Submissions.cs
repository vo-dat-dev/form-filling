using Microsoft.AspNetCore.Http.HttpResults;

public class Submissions : EndpointGroupBase
{
    public override void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetSubmission, "{id}");
    }

    [EndpointName(nameof(GetSubmission))]
    [EndpointSummary("Get a submission by id")]
    public async Task<Results<Ok<SubmissionInfo>, NotFound>> GetSubmission(DbService db, string id)
    {
        var submission = await db.GetSubmission(id);
        return submission != null ? TypedResults.Ok(submission) : TypedResults.NotFound();
    }
}