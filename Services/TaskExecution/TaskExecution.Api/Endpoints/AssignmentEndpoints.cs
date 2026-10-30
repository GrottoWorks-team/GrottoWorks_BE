using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Application.Assignments;

namespace TaskExecution.Api.Endpoints;

public sealed class CreateAssignmentRequest : IValidatableObject
{
    [Required] public Guid? VolunteerId { get; init; }
    [StringLength(2000)] public string? Note { get; init; }
    public bool OverrideCapacity { get; init; }
    public bool OverrideSkillMismatch { get; init; }
    [StringLength(500)] public string? OverrideReason { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (VolunteerId == Guid.Empty) yield return new("Volunteer ID must not be empty.", [nameof(VolunteerId)]);
        if ((OverrideCapacity || OverrideSkillMismatch) && string.IsNullOrWhiteSpace(OverrideReason))
            yield return new("Override reason is required.", [nameof(OverrideReason)]);
    }
}

public static class AssignmentEndpoints
{
    public static void MapAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Tasks").RequireAuthorization();
        group.MapPost("/tasks/{taskId:guid}/assignments", AssignAsync).WithName("assignVolunteer")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<AssignmentDto>>(201).Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapGet("/tasks/{taskId:guid}/assignments", ListAsync).WithName("listTaskAssignments")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<IReadOnlyList<AssignmentDto>>>().Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(404);
    }

    private static async Task<IResult> AssignAsync(Guid taskId, CreateAssignmentRequest request,
        AssignmentService service, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await service.AssignAsync(new AssignVolunteerCommand(taskId, request.VolunteerId!.Value,
            request.Note, request.OverrideCapacity, request.OverrideSkillMismatch, request.OverrideReason), cancellationToken);
        context.Response.Headers.ETag = $"\"{result.Version}\"";
        return Results.Json(ApiResults.Ok(result, context), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListAsync(Guid taskId, AssignmentService service,
        HttpContext context, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await service.ListAsync(taskId, cancellationToken), context));
}
