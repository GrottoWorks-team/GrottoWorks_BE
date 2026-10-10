using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Application.Seasons.Queries.GetSeasons;

namespace ParishCoordination.Api.Endpoints;

public static class SeasonEndpoints
{
    public static void MapSeasonEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/seasons", GetSeasonsAsync)
            .WithName("listSeasons")
            .WithTags("Parish")
            .RequireAuthorization()
            .Produces<PagedResponse<SeasonDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> GetSeasonsAsync(
        HttpContext httpContext,
        GetSeasonsQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var pagination = PaginationRequest.FromQuery(
            httpContext.Request.Query,
            "name",
            "seasonYear",
            "startDate",
            "endDate",
            "status");

        var query = new GetSeasonsQuery(
            pagination.Page,
            pagination.Size,
            pagination.Sort ?? "seasonYear,desc");

        var result = await queryHandler.HandleAsync(query, cancellationToken);

        return Results.Ok(
            ApiResults.Page(
                result.Items,
                result.TotalItems,
                pagination.Page,
                pagination.Size,
                httpContext));
    }
}
