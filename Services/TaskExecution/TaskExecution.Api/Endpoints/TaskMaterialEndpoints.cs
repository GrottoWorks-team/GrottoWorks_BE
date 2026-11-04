using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Api.Endpoints;

public sealed record TaskMaterialRequest(Guid MaterialId,
    [property: Range(typeof(decimal), "0.01", "9999999999.99")] decimal RequiredQuantity,
    [property: StringLength(2000)] string? Note);

public static class TaskMaterialEndpoints
{
    public static void MapTaskMaterialEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/v1/tasks/{taskId:guid}/materials", ReplaceAsync)
            .WithName("replaceTaskMaterials").WithTags("Tasks")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<TaskDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    }

    private static async Task<IResult> ReplaceAsync(Guid taskId, IReadOnlyList<TaskMaterialRequest> request,
        [FromHeader(Name = "If-Match")] string? ifMatch, ReplaceTaskMaterialsHandler handler,
        HttpContext context, CancellationToken cancellationToken)
    {
        var version = TaskVersionPrecondition.Resolve(null, ifMatch);
        if (request.Any(row => row is null))
            throw DomainRuleException.Validation("VALIDATION_FAILED", "Material entries must not be null.");
        var materials = request.Select(row => new TaskMaterialRequirement(row.MaterialId, row.RequiredQuantity, row.Note)).ToArray();
        var result = await handler.HandleAsync(new(taskId, materials, version), cancellationToken);
        context.Response.Headers.ETag = $"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result, context));
    }
}
