using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.WorkAreas.Commands.CreateWorkArea;
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

        return Results.Json(
            ApiResults.Ok(result.WorkArea!, httpContext),
            statusCode: StatusCodes.Status201Created);
    }

    private static string? CleanOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
