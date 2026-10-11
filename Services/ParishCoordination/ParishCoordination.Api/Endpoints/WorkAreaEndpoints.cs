using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
