using BuildingBlocks.Web;
using TaskExecution.Api.Common;
using TaskExecution.Api.Requests;
using TaskExecution.Application.Scopes;

namespace TaskExecution.Api.Endpoints;

public static class InternalScopeEndpoints
{
    public static void MapInternalScopeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal")
            .AllowAnonymous()
            .AddEndpointFilter<InternalApiKeyFilter>()
            .ExcludeFromDescription();

        group.MapPut("/season-scopes/{seasonId:guid}", PutSeasonScopeAsync)
            .WithName("internalPutTaskSeasonScope");
        group.MapPut("/work-area-scopes/{workAreaId:guid}", PutWorkAreaScopeAsync)
            .WithName("internalPutTaskWorkAreaScope");
    }

    private static async Task<IResult> PutSeasonScopeAsync(
        Guid seasonId,
        SeasonScopeSnapshotRequest request,
        HttpContext httpContext,
        ApplySeasonScopeSnapshotHandler handler,
        CancellationToken cancellationToken)
    {
        var (scope, applied) = await handler.HandleAsync(
            new SeasonScopeSnapshot(
                seasonId,
                request.ParishId!.Value,
                request.StartDate!.Value,
                request.EndDate!.Value,
                request.Status!.Value,
                request.Version),
            cancellationToken);

        httpContext.Response.Headers["X-Projection-Applied"] = applied ? "true" : "false";
        return Results.Ok(ApiResults.Ok(scope, httpContext));
    }

    private static async Task<IResult> PutWorkAreaScopeAsync(
        Guid workAreaId,
        WorkAreaScopeSnapshotRequest request,
        HttpContext httpContext,
        ApplyWorkAreaScopeSnapshotHandler handler,
        CancellationToken cancellationToken)
    {
        var (scope, applied) = await handler.HandleAsync(
            new WorkAreaScopeSnapshot(
                workAreaId,
                request.SeasonId!.Value,
                request.ParishId!.Value,
                request.Name,
                request.CommunityId,
                request.LeaderUserId,
                request.MaterialOfficerUserId,
                request.StartDate,
                request.Deadline,
                request.Status!.Value,
                request.Version),
            cancellationToken);

        httpContext.Response.Headers["X-Projection-Applied"] = applied ? "true" : "false";
        return Results.Ok(ApiResults.Ok(scope, httpContext));
    }
}
