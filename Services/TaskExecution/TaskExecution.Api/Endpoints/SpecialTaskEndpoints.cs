using System.ComponentModel.DataAnnotations;
using TaskExecution.Domain.Tasks;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Api.Requests;
using TaskExecution.Application.Tasks;

namespace TaskExecution.Api.Endpoints;

public sealed record CreatePurchaseTaskRequest([property:Required,StringLength(160,MinimumLength=1)] string Name,
    [property:StringLength(2000)] string? Description,[property:StringLength(4000)] string? CompletionCriteria,
    [property:Required] TaskPriority? Priority,[property:Required,Range(1,599999)] int? EstimatedMinutes,
    DateTimeOffset? StartAt,[property:Required] DateTimeOffset? Deadline);
public static class SpecialTaskEndpoints
{
    public static void MapSpecialTaskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/purchase-requests/{requestId:guid}/purchase-tasks",PurchaseAsync).WithName("createPurchaseTask")
            .WithTags("Tasks").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<TaskDto>>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(409).ProducesProblem(422).ProducesProblem(503);
        app.MapPost("/internal/tasks",CorrectiveAsync).WithName("internalCreateCorrectiveTask")
            .AllowAnonymous().AddEndpointFilter<InternalApiKeyFilter>().ExcludeFromDescription();
    }
    private static async Task<IResult> PurchaseAsync(Guid requestId,CreatePurchaseTaskRequest request,SpecialTaskService service,HttpContext context,CancellationToken ct)
    {
        var t=request;
        var result=await service.PurchaseAsync(requestId,new(Guid.Empty,t.Name,t.Description,t.CompletionCriteria,t.Priority!.Value,t.EstimatedMinutes!.Value,1,t.StartAt,t.Deadline!.Value),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";return Results.Json(ApiResults.Ok(result,context),statusCode:201);
    }
    private static async Task<IResult> CorrectiveAsync(CorrectiveTaskCommand request,[FromHeader(Name="Idempotency-Key")] string? key,SpecialTaskService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.CorrectiveAsync(key ?? "",request,ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";return Results.Json(ApiResults.Ok(result,context),statusCode:201);
    }
}
