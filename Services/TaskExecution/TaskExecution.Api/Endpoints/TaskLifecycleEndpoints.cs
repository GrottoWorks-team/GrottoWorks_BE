using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Api.Requests;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Tasks;

namespace TaskExecution.Api.Endpoints;

public static class TaskLifecycleEndpoints
{
    public static void MapTaskLifecycleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/tasks/{taskId:guid}/cancel",CancelAsync).WithName("cancelTask").WithTags("Tasks")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<TaskDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    }

    private static async Task<IResult> CancelAsync(Guid taskId,ReasonCommandRequest request,
        [FromHeader(Name="If-Match")] string? ifMatch,AssignmentService service,HttpContext context,CancellationToken cancellationToken)
    {
        var version=TaskVersionPrecondition.Resolve(request.Version,ifMatch);
        var result=await service.CancelTaskAsync(taskId,request.Reason!,version,cancellationToken);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,context));
    }
}
