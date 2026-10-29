using BuildingBlocks.Web;
using Readiness.Api.Common;
using Readiness.Api.Requests;
using Readiness.Application.Scope;
using Readiness.Domain.Scope;

namespace Readiness.Api.Endpoints;

/// <summary>
/// Interim projection feed (spec plan §5 "Nếu trễ thì: Vũ seed work_area_scope"): service-to-service
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
