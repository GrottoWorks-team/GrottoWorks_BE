using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Application.Communities;
using ParishCoordination.Application.Communities.Queries.GetCommunities;

namespace ParishCoordination.Api.Endpoints;

public static class CommunityEndpoints
{
    public static void MapCommunityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/parishes/{parishId}/communities", GetCommunitiesAsync)
            .WithName("listCommunities")
            .WithTags("Parish")
            .Produces<ApiResponse<IReadOnlyList<CommunityDto>>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetCommunitiesAsync(
        Guid parishId,
        HttpContext httpContext,
        GetCommunitiesQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var query = new GetCommunitiesQuery(parishId);
        var communities = await queryHandler.HandleAsync(query, cancellationToken);

        if (communities is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Parish not found",
                detail: $"Parish with ID '{parishId}' was not found.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "PARISH_NOT_FOUND"
                });
        }

        return Results.Ok(ApiResults.Ok(communities, httpContext));
    }
}
