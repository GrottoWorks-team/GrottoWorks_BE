using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Api.Endpoints;

public static class MyTasksEndpoints
{
    public static void MapMyTasksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users/me/tasks", ListAsync).WithName("listMyTasks").WithTags("Tasks")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<PagedResponse<TaskDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403);
    }

    private static async Task<IResult> ListAsync(string? status, string? taskType, Guid? seasonId, MyTasksQueryHandler handler,
        HttpContext context, CancellationToken cancellationToken)
    {
        var page = PaginationRequest.FromQuery(context.Request.Query, "createdAt", "name", "deadline");
        var result = await handler.HandleAsync(new(QueryEnum.Parse<TaskState>(status,"status"),
            QueryEnum.Parse<TaskType>(taskType,"taskType"),seasonId,page.Page,page.Size,page.Sort),cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items,result.Total,page.Page,page.Size,context));
    }
}
