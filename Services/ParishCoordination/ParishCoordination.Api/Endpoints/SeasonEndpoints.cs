using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Application.Seasons.Commands.CreateSeason;
using ParishCoordination.Application.Seasons.Queries.GetSeasons;
using ParishCoordination.Application.Seasons.Queries.GetSeason;

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

        app.MapGet("/api/v1/seasons/{seasonId}", GetSeasonAsync)
            .WithName("getSeason")
            .WithTags("Parish")
            .RequireAuthorization()
            .Produces<ApiResponse<SeasonDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        app.MapPost("/api/v1/seasons", CreateSeasonAsync)
            .WithName("createSeason")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(
                GrottoWorksRoles.Admin,
                GrottoWorksRoles.Parish))
            .Produces<ApiResponse<SeasonDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
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

    private static async Task<IResult> GetSeasonAsync(
        Guid seasonId,
        HttpContext httpContext,
        GetSeasonQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var season = await queryHandler.HandleAsync(
            new GetSeasonQuery(seasonId),
            cancellationToken);

        if (season is null)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Season not found",
                $"Season with ID '{seasonId}' was not found.",
                "SEASON_NOT_FOUND");
        }

        return Results.Ok(ApiResults.Ok(season, httpContext));
    }
    private static async Task<IResult> CreateSeasonAsync(
        CreateSeasonRequest request,
        HttpContext httpContext,
        CreateSeasonCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var command = new CreateSeasonCommand(
            request.ParishId,
            request.Name!.Trim(),
            request.SeasonYear!.Value,
            request.StartDate!.Value,
            request.EndDate!.Value,
            CleanOptionalText(request.Description),
            request.EstimatedBudget);

        var result = await commandHandler.HandleAsync(command, cancellationToken);

        if (result.Outcome == CreateSeasonOutcome.ParishNotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Parish not found",
                $"Parish with ID '{request.ParishId}' was not found.",
                "PARISH_NOT_FOUND");
        }

        if (result.Outcome == CreateSeasonOutcome.NameConflict)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season name conflict",
                "A season with the same name already exists in this parish.",
                "SEASON_NAME_CONFLICT");
        }

        if (result.Outcome == CreateSeasonOutcome.YearConflict)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season year conflict",
                "A season with the same year already exists in this parish.",
                "SEASON_YEAR_CONFLICT");
        }

        return Results.Json(
            ApiResults.Ok(result.Season!, httpContext),
            statusCode: StatusCodes.Status201Created);
    }

    private static string? CleanOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IResult CreateProblem(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string code)
    {
        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext),
                ["traceId"] = httpContext.TraceIdentifier
            });
    }
}