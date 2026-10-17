using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaCommunity;
using ParishCoordination.Application.WorkAreas.Commands.CreateWorkArea;
using ParishCoordination.Application.WorkAreas.Commands.UpdateWorkArea;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Application.WorkAreas.Queries.GetWorkArea;
using ParishCoordination.Application.WorkAreas.Queries.ListWorkAreas;

namespace ParishCoordination.Api.Endpoints;

public static class WorkAreaEndpoints
{
    public static void MapWorkAreaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/seasons/{seasonId}/work-areas", ListWorkAreasAsync)
            .WithName("listWorkAreas")
            .WithTags("Parish")
            .RequireAuthorization()
            .Produces<PagedResponse<WorkAreaDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/work-areas/{workAreaId}", GetWorkAreaAsync)
            .WithName("getWorkArea")
            .WithTags("Parish")
            .RequireAuthorization()
            .Produces<ApiResponse<WorkAreaDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/seasons/{seasonId}/work-areas", CreateWorkAreaAsync)
            .WithName("createWorkArea")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(
                GrottoWorksRoles.Admin,
                GrottoWorksRoles.Parish))
            .Produces<ApiResponse<WorkAreaDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPatch("/api/v1/work-areas/{workAreaId}", UpdateWorkAreaAsync)
            .WithName("updateWorkArea")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(
                GrottoWorksRoles.Admin,
                GrottoWorksRoles.Parish,
                GrottoWorksRoles.Leader))
            .Accepts<UpdateWorkAreaRequest>("application/json")
            .Produces<ApiResponse<WorkAreaDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPut("/api/v1/work-areas/{workAreaId}/community", AssignWorkAreaCommunityAsync)
            .WithName("assignWorkAreaCommunity")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(
                GrottoWorksRoles.Admin,
                GrottoWorksRoles.Parish))
            .Accepts<AssignWorkAreaCommunityRequest>("application/json")
            .Produces<ApiResponse<WorkAreaDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListWorkAreasAsync(
        Guid seasonId,
        HttpContext httpContext,
        ListWorkAreasQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var pagination = PaginationRequest.FromQuery(
            httpContext.Request.Query,
            "name",
            "workAreaName",
            "status");

        var result = await queryHandler.HandleAsync(
            new ListWorkAreasQuery(
                seasonId,
                pagination.Page,
                pagination.Size,
                pagination.Sort ?? "workAreaName,asc"),
            cancellationToken);

        if (result is null)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Season not found",
                $"Season with ID '{seasonId}' was not found.",
                "SEASON_NOT_FOUND");
        }

        return Results.Ok(
            ApiResults.Page(
                result.Items,
                result.TotalItems,
                pagination.Page,
                pagination.Size,
                httpContext));
    }

    private static async Task<IResult> GetWorkAreaAsync(
        Guid workAreaId,
        HttpContext httpContext,
        GetWorkAreaQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var workArea = await queryHandler.HandleAsync(
            new GetWorkAreaQuery(workAreaId),
            cancellationToken);

        if (workArea is null)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Work area not found",
                $"Work area with ID '{workAreaId}' was not found.",
                "WORK_AREA_NOT_FOUND");
        }

        SetETag(httpContext, workArea.Version);

        return Results.Ok(ApiResults.Ok(workArea, httpContext));
    }

    private static async Task<IResult> CreateWorkAreaAsync(
        Guid seasonId,
        CreateWorkAreaRequest request,
        HttpContext httpContext,
        CreateWorkAreaCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var result = await commandHandler.HandleAsync(
            new CreateWorkAreaCommand(
                seasonId,
                request.Name!.Trim(),
                CleanOptionalText(request.Description),
                request.CategoryId,
                request.CommunityId,
                request.StartDate,
                request.Deadline),
            cancellationToken);

        if (result.Outcome == CreateWorkAreaOutcome.SeasonNotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Season not found",
                $"Season with ID '{seasonId}' was not found.",
                "SEASON_NOT_FOUND");
        }

        if (result.Outcome == CreateWorkAreaOutcome.CommunityNotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Community not found",
                $"Community with ID '{request.CommunityId}' was not found.",
                "COMMUNITY_NOT_FOUND");
        }

        if (result.Outcome == CreateWorkAreaOutcome.SeasonInvalidState)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season state does not allow work areas",
                "Work areas can only be created for DRAFT or PLANNING seasons.",
                "SEASON_INVALID_STATE");
        }

        var workArea = result.WorkArea!;
        SetETag(httpContext, workArea.Version);

        return Results.Json(
            ApiResults.Ok(workArea, httpContext),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateWorkAreaAsync(
        Guid workAreaId,
        UpdateWorkAreaRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpContext httpContext,
        UpdateWorkAreaCommandHandler commandHandler,
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

        var command = new UpdateWorkAreaCommand(
            workAreaId,
            request.Name?.Trim(),
            request.Description is not null,
            CleanOptionalText(request.Description),
            request.CategoryId,
            request.StartDate,
            request.Deadline,
            expectedVersion);

        var result = await commandHandler.HandleAsync(command, cancellationToken);

        if (result.Outcome == UpdateWorkAreaOutcome.NotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Work area not found",
                $"Work area with ID '{workAreaId}' was not found.",
                "WORK_AREA_NOT_FOUND");
        }

        if (result.Outcome == UpdateWorkAreaOutcome.SeasonNotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Season not found",
                "The season that owns this work area was not found.",
                "SEASON_NOT_FOUND");
        }

        if (result.Outcome == UpdateWorkAreaOutcome.SeasonInvalidState)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season cannot be changed",
                "Work areas in an archived season are read-only.",
                "SEASON_INVALID_STATE");
        }

        if (result.Outcome == UpdateWorkAreaOutcome.CategoryNotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Work area category not found",
                $"Work area category with ID '{request.CategoryId}' was not found.",
                "WORK_AREA_CATEGORY_NOT_FOUND");
        }

        if (result.Outcome == UpdateWorkAreaOutcome.CategoryInactive)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Work area category is inactive",
                "An inactive work area category cannot be assigned.",
                "WORK_AREA_CATEGORY_INACTIVE");
        }

        if (result.Outcome == UpdateWorkAreaOutcome.VersionMismatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status412PreconditionFailed,
                "Work area version mismatch",
                "The work area was changed by another request. Load it again and retry.",
                "WORK_AREA_VERSION_MISMATCH");
        }

        var workArea = result.WorkArea!;
        SetETag(httpContext, workArea.Version);

        return Results.Ok(ApiResults.Ok(workArea, httpContext));
    }

    private static async Task<IResult> AssignWorkAreaCommunityAsync(
        Guid workAreaId,
        AssignWorkAreaCommunityRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpContext httpContext,
        AssignWorkAreaCommunityCommandHandler commandHandler,
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
            new AssignWorkAreaCommunityCommand(
                workAreaId,
                request.CommunityId!.Value,
                expectedVersion),
            cancellationToken);

        if (result.Outcome == AssignWorkAreaCommunityOutcome.WorkAreaNotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Work area not found",
                $"Work area with ID '{workAreaId}' was not found.",
                "WORK_AREA_NOT_FOUND");
        }

        if (result.Outcome == AssignWorkAreaCommunityOutcome.SeasonNotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Season not found",
                "The season that owns this work area was not found.",
                "SEASON_NOT_FOUND");
        }

        if (result.Outcome == AssignWorkAreaCommunityOutcome.SeasonInvalidState)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Season cannot be changed",
                "Work areas in an archived season are read-only.",
                "SEASON_INVALID_STATE");
        }

        if (result.Outcome == AssignWorkAreaCommunityOutcome.CommunityNotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Community not found",
                $"Community with ID '{request.CommunityId}' was not found.",
                "COMMUNITY_NOT_FOUND");
        }

        if (result.Outcome == AssignWorkAreaCommunityOutcome.CommunityInactive)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Community is inactive",
                "An inactive community cannot be assigned to a work area.",
                "COMMUNITY_INACTIVE");
        }

        if (result.Outcome == AssignWorkAreaCommunityOutcome.VersionMismatch)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status412PreconditionFailed,
                "Work area version mismatch",
                "The work area was changed by another request. Load it again and retry.",
                "WORK_AREA_VERSION_MISMATCH");
        }

        var workArea = result.WorkArea!;
        SetETag(httpContext, workArea.Version);

        return Results.Ok(ApiResults.Ok(workArea, httpContext));
    }

    private static string? CleanOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
