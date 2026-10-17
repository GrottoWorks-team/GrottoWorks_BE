using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Application.WorkAreaCategories;
using ParishCoordination.Application.WorkAreaCategories.Queries.ListWorkAreaCategories;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Api.Endpoints;

public static class WorkAreaCategoryEndpoints
{
    public static void MapWorkAreaCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/work-area-categories", ListWorkAreaCategoriesAsync)
            .WithName("listWorkAreaCategories")
            .WithTags("Parish")
            .RequireAuthorization()
            .Produces<ApiResponse<IReadOnlyList<WorkAreaCategoryDto>>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> ListWorkAreaCategoriesAsync(
        WorkAreaCategoryStatus? status,
        HttpContext httpContext,
        ListWorkAreaCategoriesQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var categories = await queryHandler.HandleAsync(
            new ListWorkAreaCategoriesQuery(status),
            cancellationToken);

        return Results.Ok(ApiResults.Ok(categories, httpContext));
    }
}
