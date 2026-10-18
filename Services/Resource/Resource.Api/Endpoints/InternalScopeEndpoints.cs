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
