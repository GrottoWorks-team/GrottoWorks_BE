using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Api.Requests;
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

        group.MapPost("/work-areas/{workAreaId:guid}/tasks", CreateTaskAsync)
            .WithName("createTask")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<TaskDto>>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/tasks/{taskId:guid}", UpdateTaskAsync)
            .WithName("updateTask")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<TaskDto>>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status412PreconditionFailed)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity);
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

    private static async Task<IResult> CreateTaskAsync(
        Guid workAreaId,
        CreateTaskRequest request,
        HttpContext httpContext,
        CreateTaskCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var task = await handler.HandleAsync(
            new CreateTaskCommand(
                workAreaId,
                request.Name,
                request.Description,
                request.CompletionCriteria,
                request.Priority!.Value,
                request.EstimatedMinutes,
                request.RequiredVolunteers,
                request.StartAt,
                request.Deadline),
            cancellationToken);

        httpContext.Response.Headers.ETag = $"\"{task.Version}\"";
        return Results.Created($"/api/v1/tasks/{task.Id}", ApiResults.Ok(task, httpContext));
    }

    private static async Task<IResult> UpdateTaskAsync(
        Guid taskId,
        UpdateTaskRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpContext httpContext,
        UpdateTaskCommandHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TaskVersionPrecondition.TryParse(ifMatch, out var expectedVersion))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid If-Match header",
                detail: "If-Match must contain a positive integer version.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "INVALID_IF_MATCH",
                    ["correlationId"] = CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext),
                    ["traceId"] = httpContext.TraceIdentifier
                });
        }

        var task = await handler.HandleAsync(
            new UpdateTaskCommand(
                taskId,
                request.Name,
                request.Description,
                request.CompletionCriteria,
                request.Priority,
                request.EstimatedMinutes,
                request.RequiredVolunteers,
                request.StartAt,
                request.Deadline,
                expectedVersion),
            cancellationToken);

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
