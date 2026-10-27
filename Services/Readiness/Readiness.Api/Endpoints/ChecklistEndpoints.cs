using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Readiness.Api.Common;
using Readiness.Api.Requests;
using Readiness.Application.Checklists;

namespace Readiness.Api.Endpoints;

/// <summary>F-RDY-02..06: checklists, inspection, corrective tasks, confirm / reopen, season readiness (tag Readiness).</summary>
public static class ChecklistEndpoints
{
    private const string VersionMismatch = "READINESS_VERSION_MISMATCH";

    public static void MapChecklistEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/work-areas/{workAreaId:guid}/readiness-checks", CreateAsync)
            .WithName("createReadinessCheck")
            .WithTags("Readiness")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Accepts<CreateReadinessCheckRequest>("application/json")
            .Produces<ApiResponse<ReadinessCheckDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/seasons/{seasonId:guid}/readiness", GetSeasonAsync)
            .WithName("getSeasonReadiness")
            .WithTags("Readiness")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer))
            .Produces<ApiResponse<SeasonReadinessDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var group = app.MapGroup("/api/v1/readiness-checks/{checkId:guid}").WithTags("Readiness");

        group.MapGet("", GetAsync)
            .WithName("getReadinessCheck")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer))
            .Produces<ApiResponse<ReadinessCheckDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/items", ReplaceItemsAsync)
            .WithName("replaceReadinessItems")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Accepts<List<ReadinessItemDefinitionRequest>>("application/json")
            .Produces<ApiResponse<ReadinessCheckDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/items/{itemId:guid}", RecordResultAsync)
            .WithName("recordReadinessResult")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Accepts<ReadinessItemResultRequest>("application/json")
            .Produces<ApiResponse<ReadinessItemDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/items/{itemId:guid}/evidence", AttachEvidenceAsync)
            .WithName("attachReadinessEvidence")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader, GrottoWorksRoles.Volunteer))
            .Accepts<List<FileReferenceRequest>>("application/json")
            .Produces<ApiResponse<ReadinessItemDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/items/{itemId:guid}/corrective-task", LinkCorrectiveTaskAsync)
            .WithName("linkCorrectiveTask")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Accepts<CorrectiveTaskLinkRequest>("application/json")
            .Produces<ApiResponse<ReadinessItemDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/items/{itemId:guid}/recheck", RecheckAsync)
            .WithName("recheckReadinessItem")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Accepts<ReadinessItemResultRequest>("application/json")
            .Produces<ApiResponse<ReadinessItemDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/confirm", ConfirmAsync)
            .WithName("confirmAreaReadiness")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Accepts<VersionedCommandRequest>("application/json")
            .Produces<ApiResponse<ReadinessCheckDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        group.MapPost("/reopen", ReopenAsync)
            .WithName("reopenReadinessCheck")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish, GrottoWorksRoles.Leader))
            .Accepts<ReasonCommandRequest>("application/json")
            .Produces<ApiResponse<ReadinessCheckDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> CreateAsync(
        Guid workAreaId,
        CreateReadinessCheckRequest request,
        HttpContext httpContext,
        CreateReadinessCheckCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var check = await handler.HandleAsync(
            new CreateReadinessCheckCommand(workAreaId, request.Name, request.Items!.Select(item => item.ToDefinition()).ToList()),
            cancellationToken);
        return Results.Json(ApiResults.Ok(check, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetSeasonAsync(
        Guid seasonId,
        HttpContext httpContext,
        GetSeasonReadinessQueryHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(seasonId, cancellationToken), httpContext));

    private static async Task<IResult> GetAsync(
        Guid checkId,
        HttpContext httpContext,
        ChecklistDefinitionHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.GetAsync(checkId, cancellationToken), httpContext));

    /// <summary>The body is a bare <c>ReadinessItemDefinition[]</c>, so the version comes from <c>If-Match</c>.</summary>
    private static async Task<IResult> ReplaceItemsAsync(
        Guid checkId,
        List<ReadinessItemDefinitionRequest> items,
        HttpContext httpContext,
        ChecklistDefinitionHandler handler,
        CancellationToken cancellationToken)
    {
        var check = await handler.ReplaceItemsAsync(
            new ReplaceItemsCommand(checkId, VersionHeader.Resolve(null, httpContext, VersionMismatch), items.Select(item => item.ToDefinition()).ToList()),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(check, httpContext));
    }

    private static async Task<IResult> RecordResultAsync(
        Guid checkId,
        Guid itemId,
        ReadinessItemResultRequest request,
        HttpContext httpContext,
        InspectionCommandHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.RecordResultAsync(ToCommand(checkId, itemId, request, httpContext), cancellationToken), httpContext));

    private static async Task<IResult> RecheckAsync(
        Guid checkId,
        Guid itemId,
        ReadinessItemResultRequest request,
        HttpContext httpContext,
        InspectionCommandHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.RecheckAsync(ToCommand(checkId, itemId, request, httpContext), cancellationToken), httpContext));

    private static async Task<IResult> AttachEvidenceAsync(
        Guid checkId,
        Guid itemId,
        List<FileReferenceRequest> files,
        HttpContext httpContext,
        AttachEvidenceCommandHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.HandleAsync(new AttachEvidenceCommand(checkId, itemId, files.Select(file => file.Id!.Value).ToList()), cancellationToken),
            httpContext));

    private static async Task<IResult> LinkCorrectiveTaskAsync(
        Guid checkId,
        Guid itemId,
        CorrectiveTaskLinkRequest request,
        HttpContext httpContext,
        InspectionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var item = await handler.LinkCorrectiveTaskAsync(
            new LinkCorrectiveTaskCommand(checkId, itemId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.TaskId),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(item, httpContext));
    }

    private static async Task<IResult> ConfirmAsync(
        Guid checkId,
        VersionedCommandRequest request,
        HttpContext httpContext,
        ReadinessDecisionHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.ConfirmAsync(new CheckCommand(checkId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch)), cancellationToken),
            httpContext));

    private static async Task<IResult> ReopenAsync(
        Guid checkId,
        ReasonCommandRequest request,
        HttpContext httpContext,
        ReadinessDecisionHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.ReopenAsync(
                new CheckCommand(checkId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.Reason),
                cancellationToken),
            httpContext));

    private static ItemResultCommand ToCommand(Guid checkId, Guid itemId, ReadinessItemResultRequest request, HttpContext httpContext) =>
        new(checkId, itemId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.Result!.Value, request.Note,
            request.EvidenceFileIds ?? []);
}
