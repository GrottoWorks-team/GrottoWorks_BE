using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Reporting.Api.Common;
using Reporting.Application.Recognition;

namespace Reporting.Api.Endpoints;

/// <summary>F-RPT-03: recognition of the caller and of a season (tag Reports).</summary>
public static class RecognitionEndpoints
{
    public static void MapRecognitionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users/me/recognition", GetMineAsync)
            .WithName("getMyRecognition")
            .WithTags("Reports")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<RecognitionSummaryDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/v1/seasons/{seasonId:guid}/recognition", GetSeasonAsync)
            .WithName("getSeasonRecognition")
            .WithTags("Reports")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish))
            .Produces<ApiResponse<RecognitionRankingDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetMineAsync(HttpContext httpContext, RecognitionQueryHandler handler, CancellationToken cancellationToken)
    {
        var seasonId = new QueryReader(httpContext.Request.Query, "seasonId").Guid("seasonId");
        return Results.Ok(ApiResults.Ok(await handler.GetMineAsync(seasonId, cancellationToken), httpContext));
    }

    private static async Task<IResult> GetSeasonAsync(
        Guid seasonId,
        HttpContext httpContext,
        RecognitionQueryHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.GetSeasonAsync(seasonId, cancellationToken), httpContext));
}
