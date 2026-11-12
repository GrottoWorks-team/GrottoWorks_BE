using System.Text.Json.Serialization;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Tasks;

namespace TaskExecution.Api.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SetTaskTeamLeaderRequest
{
    public required Guid TeamLeaderAssignmentId { get; init; }
}

public static class TaskTeamLeaderEndpoints
{
    public static void MapTaskTeamLeaderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tasks/{taskId:guid}/team-leader")
            .WithTags("Task")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader));

        group.MapPut("", SetAsync).WithName("setTaskTeamLeader")
            .Accepts<SetTaskTeamLeaderRequest>("application/json")
            .Produces<ApiResponse<TaskDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(503);
        group.MapDelete("", ClearAsync).WithName("clearTaskTeamLeader")
            .Produces<ApiResponse<TaskDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(503);
    }

    private static async Task<IResult> SetAsync(Guid taskId, SetTaskTeamLeaderRequest request,
        [FromHeader(Name = "If-Match")] string ifMatch, TaskTeamLeaderService service,
        HttpContext context, CancellationToken cancellationToken)
    {
        var version = RequireVersion(ifMatch);
        if (request.TeamLeaderAssignmentId == Guid.Empty)
            throw new DomainException("VALIDATION_FAILED", "teamLeaderAssignmentId must be a non-empty UUID.", 400);

        var result = await service.SetAsync(taskId, request.TeamLeaderAssignmentId, version, cancellationToken);
        return WriteResult(context, result);
    }

    private static async Task<IResult> ClearAsync(Guid taskId,
        [FromHeader(Name = "If-Match")] string ifMatch, TaskTeamLeaderService service,
        HttpContext context, CancellationToken cancellationToken)
    {
        var version = RequireVersion(ifMatch);
        if (await context.Request.Body.ReadAsync(new byte[1], cancellationToken) != 0)
            throw new DomainException("VALIDATION_FAILED", "Clearing a task team leader does not accept a request body.", 400);

        var result = await service.ClearAsync(taskId, version, cancellationToken);
        return WriteResult(context, result);
    }

    private static int RequireVersion(string ifMatch)
    {
        if (!TaskVersionPrecondition.TryParse(ifMatch, out var version) || !version.HasValue)
            throw new DomainException("INVALID_IF_MATCH", "If-Match must contain a positive integer version.", 400);

        return version.Value;
    }

    private static IResult WriteResult(HttpContext context, TaskDto result)
    {
        context.Response.Headers.ETag = $"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result, context));
    }
}
