using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Api.Responses;
using ParishCoordination.Application.Parishes.GetParishes;

namespace ParishCoordination.Api.Endpoints;

public static class ParishEndpoints
{
    public static void MapParishEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/parishes", GetAllParishesAsync)
            .WithName("listParishes")
            .WithTags("Parish")
            .Produces<ParishListResponse>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetAllParishesAsync(
        GetParishesQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var query = new GetParishesQuery();
        var parishes = await queryHandler.HandleAsync(query, cancellationToken);
        var response = new ParishListResponse(parishes);

        return Results.Ok(response);
    }
}
