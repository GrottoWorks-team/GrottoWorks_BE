using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Api.Endpoints;

public static class TaskEndpoints
{
    private static readonly string[] SortFields = ["name", "deadline", "status", "priority", "createdAt"];

    public static void MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithTags("Tasks");

        group.MapGet("/work-areas/{workAreaId:guid}/tasks", ListTasksAsync)
            .WithName("listTasks")
            .Produces<PagedResponse<TaskDto>>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapGet("/tasks/{taskId:guid}", GetTaskAsync)
            .WithName("getTask")
            .Produces<ApiResponse<TaskDto>>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListTasksAsync(
        Guid workAreaId,
        string? status,
        string? taskType,
        HttpContext httpContext,
        GetTasksQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var pagination = PaginationRequest.FromQuery(httpContext.Request.Query, SortFields);
        var query = new GetTasksQuery(
            workAreaId,
            ParseEnum<TaskState>(status, "status"),
            ParseEnum<TaskType>(taskType, "taskType"),
            pagination.Page,
            pagination.Size,
            pagination.Sort);

        var (items, page) = await handler.HandleAsync(query, cancellationToken);
        return Results.Ok(ApiResults.Page(items, page.TotalItems, page.Page, page.Size, httpContext));
    }

    private static async Task<IResult> GetTaskAsync(
        Guid taskId,
        HttpContext httpContext,
        GetTaskQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var task = await handler.HandleAsync(taskId, cancellationToken);
        httpContext.Response.Headers.ETag = $"\"{task.Version}\"";
        return Results.Ok(ApiResults.Ok(task, httpContext));
    }

    private static TEnum? ParseEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace("_", string.Empty);
        if (Enum.TryParse<TEnum>(normalized, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new DomainException(
            "INVALID_FILTER_VALUE",
            $"'{field}' has an unsupported value '{value}'.",
            StatusCodes.Status400BadRequest);
    }
}
