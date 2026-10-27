using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Scope;
using Resource.Domain.Scope;

namespace Resource.Api.Endpoints;

/// <summary>
/// F-RES-01 interim projection feed (spec plan §5 "Nếu trễ thì: Vũ seed work_area_scope"): service-to-service
/// only (<c>X-Internal-Api-Key</c>), no gateway route, hidden from OpenAPI. The MassTransit consumers of
/// <c>planning.work-area.*</c> / <c>planning.season.*</c> will call the same handlers.
/// </summary>
public static class InternalScopeEndpoints
{
    public static void MapInternalScopeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal")
            .AllowAnonymous()
            .AddEndpointFilter<InternalApiKeyFilter>()
            .ExcludeFromDescription();

        group.MapPut("/work-area-scopes/{workAreaId:guid}", PutWorkAreaAsync).WithName("internalPutWorkAreaScope");
        group.MapPut("/season-scopes/{seasonId:guid}", PutSeasonAsync).WithName("internalPutSeasonStatus");
        group.MapPut("/task-scopes/{taskId:guid}", PutTaskAsync).WithName("internalPutTaskScope");
        group.MapPost("/support-transfers", StartSupportTransferAsync).WithName("internalStartSupportTransfer");
    }

    private static async Task<IResult> PutWorkAreaAsync(
        Guid workAreaId,
        WorkAreaSnapshotRequest request,
        HttpContext httpContext,
        ApplyWorkAreaSnapshotHandler handler,
        CancellationToken cancellationToken)
    {
        var (scope, applied) = await handler.HandleAsync(
            new WorkAreaSnapshot(
                workAreaId,
                request.SeasonId!.Value,
                request.ParishId!.Value,
                request.Name,
                request.Status!.Value,
                request.SeasonStatus!.Value,
                request.CommunityId,
                request.LeaderUserId,
                request.MaterialOfficerUserId,
                request.Version),
            cancellationToken);

        httpContext.Response.Headers["X-Projection-Applied"] = applied ? "true" : "false";
        return Results.Ok(ApiResults.Ok(scope, httpContext));
    }

    private static async Task<IResult> PutTaskAsync(
        Guid taskId,
        TaskSnapshotRequest request,
        HttpContext httpContext,
        ApplyTaskSnapshotHandler handler,
        CancellationToken cancellationToken)
    {
        var (task, applied) = await handler.HandleAsync(
            new TaskSnapshot(taskId, request.WorkAreaId!.Value, request.Name, request.Type!.Value, request.Status!.Value,
                request.PurchaseRequestId, request.AssigneeUserIds ?? [], request.Version),
            cancellationToken);

        httpContext.Response.Headers["X-Projection-Applied"] = applied ? "true" : "false";
        return Results.Ok(ApiResults.Ok(task, httpContext));
    }

    /// <summary>
    /// F-RES-20: TaskExecution approves a support material → TRANSFER_OUT on the source work area. 201 when posted,
    /// 200 on an identical replay (idempotent by support response material id), 409 when stock is short or data differ.
    /// </summary>
    private static async Task<IResult> StartSupportTransferAsync(
        StartSupportTransferRequest request,
        HttpContext httpContext,
        Application.Inventory.StartSupportTransferCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var (transfer, created) = await handler.HandleAsync(
            new Application.Inventory.StartSupportTransferCommand(request.SupportResponseMaterialId!.Value, request.SourceWorkAreaId!.Value,
                request.TargetWorkAreaId!.Value, request.MaterialId!.Value, request.Quantity!.Value, request.ApprovedByUserId!.Value),
            cancellationToken);
        return Results.Json(ApiResults.Ok(transfer, httpContext), statusCode: created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static async Task<IResult> PutSeasonAsync(
        Guid seasonId,
        SeasonStatusRequest request,
        HttpContext httpContext,
        ApplySeasonStatusHandler handler,
        CancellationToken cancellationToken)
    {
        var updated = await handler.HandleAsync(new SeasonStatusChange(seasonId, request.Status!.Value), cancellationToken);
        return Results.Ok(ApiResults.Ok(new { seasonId, workAreasUpdated = updated }, httpContext));
    }
}
