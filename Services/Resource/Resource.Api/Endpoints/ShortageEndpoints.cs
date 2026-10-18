using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Application.Shortages;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-05: calculated shortages (tag Materials).</summary>
public static class ShortageEndpoints
{
    public static void MapShortageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/work-areas/{workAreaId:guid}/shortages", GetWorkAreaShortagesAsync)
            .WithName("getWorkAreaShortages")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer))
            .Produces<ApiResponse<IReadOnlyList<MaterialShortageDto>>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/seasons/{seasonId:guid}/material-needs", ListMaterialNeedsAsync)
            .WithName("listMaterialNeeds")
            .WithTags("Materials")
            .RequireAuthorization()
            .Produces<PagedResponse<MaterialNeedDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> GetWorkAreaShortagesAsync(
        Guid workAreaId,
        HttpContext httpContext,
        GetWorkAreaShortagesQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _ = new QueryReader(httpContext.Request.Query);
        var shortages = await handler.HandleAsync(workAreaId, cancellationToken);
        return Results.Ok(ApiResults.Ok(shortages, httpContext));
    }

    /// <summary><c>MaterialNeedFilter</c>: workAreaId, materialId; paged; only positive shortages.</summary>
    private static async Task<IResult> ListMaterialNeedsAsync(
        Guid seasonId,
        HttpContext httpContext,
        ListMaterialNeedsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "workAreaId", "materialId");
        var (page, size) = reader.Paging();
        var result = await handler.HandleAsync(
            new MaterialNeedFilter(page, size, seasonId, reader.Guid("workAreaId"), reader.Guid("materialId")),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }
}
