using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Api.Common;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Application.Seasons.Commands.ActivateSeason;
using ParishCoordination.Application.Seasons.Commands.ArchiveSeason;
using ParishCoordination.Application.Seasons.Commands.CompleteSeason;
using ParishCoordination.Application.Seasons.Commands.CreateSeason;
using ParishCoordination.Application.Seasons.Commands.UpdateSeason;
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

        app.MapPatch("/api/v1/seasons/{seasonId}", UpdateSeasonAsync)
            .WithName("updateSeason")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(
                GrottoWorksRoles.Admin,
                GrottoWorksRoles.Parish))
            .Accepts<UpdateSeasonRequest>("application/json")
            .Produces<ApiResponse<SeasonDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/v1/seasons/{seasonId}/activate", ActivateSeasonAsync)
            .WithName("activateSeason")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(
                GrottoWorksRoles.Admin,
                GrottoWorksRoles.Parish))
            .Accepts<VersionedCommandRequest>("application/json")
            .Produces<ApiResponse<SeasonDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/v1/seasons/{seasonId}/complete", CompleteSeasonAsync)
            .WithName("completeSeason")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(
                GrottoWorksRoles.Admin,
                GrottoWorksRoles.Parish))
            .Accepts<VersionedCommandRequest>("application/json")
            .Produces<ApiResponse<SeasonDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/v1/seasons/{seasonId}/archive", ArchiveSeasonAsync)
            .WithName("archiveSeason")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(
                GrottoWorksRoles.Admin,
                GrottoWorksRoles.Parish))
            .Accepts<VersionedCommandRequest>("application/json")
            .Produces<ApiResponse<SeasonDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
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

        SetETag(httpContext, season.Version);

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

        var season = result.Season!;
        SetETag(httpContext, season.Version);

        return Results.Json(
            ApiResults.Ok(season, httpContext),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateSeasonAsync(
        Guid seasonId,
        UpdateSeasonRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpContext httpContext,
        UpdateSeasonCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        if (!TryParseExpectedVersion(ifMatch, out var expectedVersion))
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Invalid If-Match header",
                "If-Match must contain a positive integer version.",
                "INVALID_IF_MATCH");
        }

        var result = await commandHandler.HandleAsync(
            new UpdateSeasonCommand(
                seasonId,
                request.Name?.Trim(),
                request.SeasonYear,
                request.StartDate,
                request.EndDate,
                request.Description is not null,
                CleanOptionalText(request.Description),
                request.EstimatedBudget,
                expectedVersion),
            cancellationToken);

        if (result.Outcome == UpdateSeasonOutcome.NotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Season not found",
                $"Season with ID '{seasonId}' was not found.",
                "SEASON_NOT_FOUND");
        }

        if (result.Outcome == UpdateSeasonOutcome.InvalidState)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season cannot be updated",
                "Only seasons in PLANNING status can be updated.",
                "SEASON_INVALID_STATE");
        }

        if (result.Outcome == UpdateSeasonOutcome.InvalidDateRange)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                "Invalid season date range",
                "End date must be after start date.",
                "SEASON_INVALID_DATE_RANGE");
        }

        if (result.Outcome == UpdateSeasonOutcome.NameConflict)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season name conflict",
                "A season with the same name already exists in this parish.",
                "SEASON_NAME_CONFLICT");
        }

        if (result.Outcome == UpdateSeasonOutcome.YearConflict)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season year conflict",
                "A season with the same year already exists in this parish.",
                "SEASON_YEAR_CONFLICT");
        }

        if (result.Outcome == UpdateSeasonOutcome.VersionMismatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status412PreconditionFailed,
                "Season version mismatch",
                "The season was changed by another request. Load it again and retry.",
                "SEASON_VERSION_MISMATCH");
        }

        var season = result.Season!;
        SetETag(httpContext, season.Version);

        return Results.Ok(ApiResults.Ok(season, httpContext));
    }

    private static async Task<IResult> ActivateSeasonAsync(
        Guid seasonId,
        VersionedCommandRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpContext httpContext,
        ActivateSeasonCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var versionPrecondition = SeasonVersionPrecondition.Resolve(
            request.Version,
            ifMatch);

        if (versionPrecondition.Error is SeasonVersionPreconditionError.InvalidIfMatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Invalid If-Match header",
                "If-Match must contain a positive integer version.",
                "INVALID_IF_MATCH");
        }

        if (versionPrecondition.Error is SeasonVersionPreconditionError.VersionMismatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status412PreconditionFailed,
                "Season version mismatch",
                "If-Match and body version disagree. Load the season again and retry.",
                "SEASON_VERSION_MISMATCH");
        }

        var result = await commandHandler.HandleAsync(
            new ActivateSeasonCommand(seasonId, versionPrecondition.Version),
            cancellationToken);

        if (result.Outcome == ActivateSeasonOutcome.NotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Season not found",
                $"Season with ID '{seasonId}' was not found.",
                "SEASON_NOT_FOUND");
        }

        if (result.Outcome == ActivateSeasonOutcome.InvalidState)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season cannot be activated",
                "Only seasons in PLANNING status can be activated.",
                "SEASON_INVALID_STATE");
        }

        if (result.Outcome == ActivateSeasonOutcome.WorkAreaRequired)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season requires a work area",
                "At least one work area is required before the season can be activated.",
                "SEASON_WORK_AREA_REQUIRED");
        }

        if (result.Outcome == ActivateSeasonOutcome.WorkAreaAssignmentIncomplete)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Work area assignment is incomplete",
                "Every work area must have a leader and material officer before activation.",
                "WORK_AREA_ASSIGNMENT_INCOMPLETE");
        }

        if (result.Outcome == ActivateSeasonOutcome.ActivePeriodConflict)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season active period conflicts",
                "Another active season in this parish overlaps the requested season period.",
                "SEASON_ACTIVE_PERIOD_CONFLICT");
        }

        if (result.Outcome == ActivateSeasonOutcome.VersionMismatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status412PreconditionFailed,
                "Season version mismatch",
                "The season was changed by another request. Load it again and retry.",
                "SEASON_VERSION_MISMATCH");
        }

        var season = result.Season!;
        SetETag(httpContext, season.Version);

        return Results.Ok(ApiResults.Ok(season, httpContext));
    }

    private static async Task<IResult> CompleteSeasonAsync(
        Guid seasonId,
        VersionedCommandRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpContext httpContext,
        CompleteSeasonCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var versionPrecondition = SeasonVersionPrecondition.Resolve(
            request.Version,
            ifMatch);

        if (versionPrecondition.Error is SeasonVersionPreconditionError.InvalidIfMatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Invalid If-Match header",
                "If-Match must contain a positive integer version.",
                "INVALID_IF_MATCH");
        }

        if (versionPrecondition.Error is SeasonVersionPreconditionError.VersionMismatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status412PreconditionFailed,
                "Season version mismatch",
                "If-Match and body version disagree. Load the season again and retry.",
                "SEASON_VERSION_MISMATCH");
        }

        var result = await commandHandler.HandleAsync(
            new CompleteSeasonCommand(seasonId, versionPrecondition.Version),
            cancellationToken);

        if (result.Outcome == CompleteSeasonOutcome.NotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Season not found",
                $"Season with ID '{seasonId}' was not found.",
                "SEASON_NOT_FOUND");
        }

        if (result.Outcome == CompleteSeasonOutcome.InvalidState)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season cannot be completed",
                "Only seasons in READY status can be completed.",
                "SEASON_INVALID_STATE");
        }

        if (result.Outcome == CompleteSeasonOutcome.VersionMismatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status412PreconditionFailed,
                "Season version mismatch",
                "The season was changed by another request. Load it again and retry.",
                "SEASON_VERSION_MISMATCH");
        }

        var season = result.Season!;
        SetETag(httpContext, season.Version);

        return Results.Ok(ApiResults.Ok(season, httpContext));
    }

    private static async Task<IResult> ArchiveSeasonAsync(
        Guid seasonId,
        VersionedCommandRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpContext httpContext,
        ArchiveSeasonCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var versionPrecondition = SeasonVersionPrecondition.Resolve(
            request.Version,
            ifMatch);

        if (versionPrecondition.Error is SeasonVersionPreconditionError.InvalidIfMatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Invalid If-Match header",
                "If-Match must contain a positive integer version.",
                "INVALID_IF_MATCH");
        }

        if (versionPrecondition.Error is SeasonVersionPreconditionError.VersionMismatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status412PreconditionFailed,
                "Season version mismatch",
                "If-Match and body version disagree. Load the season again and retry.",
                "SEASON_VERSION_MISMATCH");
        }

        var result = await commandHandler.HandleAsync(
            new ArchiveSeasonCommand(seasonId, versionPrecondition.Version),
            cancellationToken);

        if (result.Outcome == ArchiveSeasonOutcome.NotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Season not found",
                $"Season with ID '{seasonId}' was not found.",
                "SEASON_NOT_FOUND");
        }

        if (result.Outcome == ArchiveSeasonOutcome.InvalidState)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season cannot be archived",
                "Only seasons in COMPLETED status can be archived.",
                "SEASON_INVALID_STATE");
        }

        if (result.Outcome == ArchiveSeasonOutcome.VersionMismatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status412PreconditionFailed,
                "Season version mismatch",
                "The season was changed by another request. Load it again and retry.",
                "SEASON_VERSION_MISMATCH");
        }

        var season = result.Season!;
        SetETag(httpContext, season.Version);

        return Results.Ok(ApiResults.Ok(season, httpContext));
    }

    private static string? CleanOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryParseExpectedVersion(
        string? ifMatch,
        out int? expectedVersion)
    {
        expectedVersion = null;

        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return true;
        }

        var value = ifMatch.Trim();
        var startsWithQuote = value.StartsWith('"');
        var endsWithQuote = value.EndsWith('"');

        if (startsWithQuote || endsWithQuote)
        {
            if (!startsWithQuote || !endsWithQuote || value.Length <= 2)
            {
                return false;
            }

            value = value[1..^1];
        }

        if (!int.TryParse(value, out var version) || version <= 0)
        {
            return false;
        }

        expectedVersion = version;
        return true;
    }

    private static void SetETag(HttpContext httpContext, int version)
    {
        httpContext.Response.Headers.ETag = $"\"{version}\"";
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
