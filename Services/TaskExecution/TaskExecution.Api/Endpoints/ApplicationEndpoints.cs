using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Application.Assignments;
using TaskExecution.Api.Common;
using TaskExecution.Api.Requests;

namespace TaskExecution.Api.Endpoints;

public sealed record TaskApplicationRequest([property: StringLength(2000)] string? Note);

public static class ApplicationEndpoints
{
    public static void MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Tasks").RequireAuthorization();
        group.MapPost("/task-applications/{applicationId:guid}/approve", ApproveAsync).WithName("approveTaskApplication")
            .RequireAuthorization(p => p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<AssignmentDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapPost("/task-applications/{applicationId:guid}/reject", RejectAsync).WithName("rejectTaskApplication")
            .RequireAuthorization(p => p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<TaskApplicationDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapPost("/tasks/{taskId:guid}/applications", ApplyAsync).WithName("applyForTask")
            .RequireAuthorization(p => p.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<TaskApplicationDto>>(201).Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapGet("/tasks/{taskId:guid}/applications", ListAsync).WithName("listTaskApplications")
            .RequireAuthorization(p => p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<PagedResponse<TaskApplicationDto>>().Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(404);
    }

    private static async Task<IResult> ApplyAsync(Guid taskId, TaskApplicationRequest request, AssignmentService service,
        HttpContext context, CancellationToken cancellationToken)
    {
        var result = await service.ApplyAsync(taskId, request.Note, cancellationToken);
        context.Response.Headers.ETag = $"\"{result.Version}\"";
        return Results.Json(ApiResults.Ok(result, context), statusCode: 201);
    }

    private static async Task<IResult> ApproveAsync(Guid applicationId, VersionedCommandRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, AssignmentService service, HttpContext context, CancellationToken cancellationToken)
    {
        var version = TaskVersionPrecondition.Resolve(request.Version, ifMatch, "TASK_ASSIGNMENT_VERSION_MISMATCH");
        var result = await service.ApproveApplicationAsync(applicationId, version, cancellationToken);
        context.Response.Headers.ETag = $"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result, context));
    }

    private static async Task<IResult> RejectAsync(Guid applicationId, ReasonCommandRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, AssignmentService service, HttpContext context, CancellationToken cancellationToken)
    {
        var version = TaskVersionPrecondition.Resolve(request.Version, ifMatch, "TASK_ASSIGNMENT_VERSION_MISMATCH");
        var result = await service.RejectApplicationAsync(applicationId, request.Reason!, version, cancellationToken);
        context.Response.Headers.ETag = $"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result, context));
    }

    private static async Task<IResult> ListAsync(Guid taskId, AssignmentService service, HttpContext context, CancellationToken cancellationToken)
    {
        var page = PaginationRequest.FromQuery(context.Request.Query, "requestedAt");
        var result = await service.ApplicationsAsync(taskId, page.Page, page.Size, page.Sort?.EndsWith(",desc") == true, cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.Total, page.Page, page.Size, context));
    }
}
