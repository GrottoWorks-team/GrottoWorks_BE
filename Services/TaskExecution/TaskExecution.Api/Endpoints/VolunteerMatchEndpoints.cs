using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Application.Tasks;

namespace TaskExecution.Api.Endpoints;

public static class VolunteerMatchEndpoints
{
    public static void MapVolunteerMatchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/tasks/{taskId:guid}/volunteer-matches", ListAsync)
            .WithName("listVolunteerMatches").WithTags("Tasks")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<IReadOnlyList<VolunteerMatchDto>>>()
            .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403)
            .Produces<ProblemDetails>(404).Produces<ProblemDetails>(503);
    }

    private static async Task<IResult> ListAsync(Guid taskId, Guid? communityId, int? limit,
        GetVolunteerMatchesHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(taskId, communityId, limit ?? 20, cancellationToken), context));
}
