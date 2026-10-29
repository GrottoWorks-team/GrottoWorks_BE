using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Reporting.Api.Common;
using Reporting.Application.Reports;
using Reporting.Domain;

namespace Reporting.Api.Endpoints;

/// <summary>F-RPT-04 dashboards and F-RPT-05 reports (tag Reports).</summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var dashboards = app.MapGroup("/api/v1/dashboards").WithTags("Reports");
        dashboards.MapGet("/season", SeasonDashboardAsync)
            .WithName("viewSeasonDashboard")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish))
            .Produces<ApiResponse<SeasonDashboardDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        dashboards.MapGet("/work-area", WorkAreaDashboardAsync)
            .WithName("viewWorkAreaDashboard")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<WorkAreaDashboardDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        dashboards.MapGet("/materials", MaterialDashboardAsync)
            .WithName("viewMaterialDashboard")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Produces<ApiResponse<MaterialDashboardDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var reports = app.MapGroup("/api/v1/reports").WithTags("Reports");
        Map(reports, "/tasks", "getTaskReport", ReportType.Tasks);
        Map(reports, "/materials", "getMaterialReport", ReportType.Materials);
        Map(reports, "/purchases", "getPurchaseReport", ReportType.Purchases);
        Map(reports, "/contributions", "getContributionReport", ReportType.Contributions);
        Map(reports, "/attendance", "getAttendanceReport", ReportType.Attendance);
        Map(reports, "/readiness", "getReadinessReport", ReportType.Readiness);
    }

    /// <summary><c>ReportQuery</c> from the query string: seasonId (required), workAreaId, communityId, from, to.</summary>
    public static ReportQuery ReadReportQuery(QueryReader reader)
    {
        var query = new ReportQuery(reader.RequiredGuid("seasonId"), reader.Guid("workAreaId"), reader.Guid("communityId"), reader.Date("from"),
            reader.Date("to"));
        if (query.From is { } from && query.To is { } to && to < from)
        {
            throw new DomainRuleException("INVALID_FILTER_VALUE", "'to' must be on or after 'from'.", DomainErrorKind.BadRequest);
        }

        return query;
    }

    private static void Map(RouteGroupBuilder group, string path, string operationId, ReportType type) =>
        group.MapGet(path, (HttpContext httpContext, ReportQueryHandler handler, CancellationToken cancellationToken) =>
                ReportAsync(type, httpContext, handler, cancellationToken))
            .WithName(operationId)
            .RequireAuthorization(policy => policy.RequireRole([.. ReportScopeResolver.RolesFor(type)]))
            .Produces<ApiResponse<ReportDataDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> ReportAsync(ReportType type, HttpContext httpContext, ReportQueryHandler handler, CancellationToken cancellationToken)
    {
        var query = ReadReportQuery(new QueryReader(httpContext.Request.Query, "seasonId", "workAreaId", "communityId", "from", "to"));
        return Results.Ok(ApiResults.Ok(await handler.BuildAsync(type, query, cancellationToken), httpContext));
    }

    private static async Task<IResult> SeasonDashboardAsync(HttpContext httpContext, DashboardQueryHandler handler, CancellationToken cancellationToken)
    {
        var seasonId = new QueryReader(httpContext.Request.Query, "seasonId").RequiredGuid("seasonId");
        return Results.Ok(ApiResults.Ok(await handler.SeasonAsync(seasonId, cancellationToken), httpContext));
    }

    private static async Task<IResult> WorkAreaDashboardAsync(HttpContext httpContext, DashboardQueryHandler handler, CancellationToken cancellationToken)
    {
        var workAreaId = new QueryReader(httpContext.Request.Query, "workAreaId").RequiredGuid("workAreaId");
        return Results.Ok(ApiResults.Ok(await handler.WorkAreaAsync(workAreaId, cancellationToken), httpContext));
    }

    private static async Task<IResult> MaterialDashboardAsync(HttpContext httpContext, DashboardQueryHandler handler, CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "workAreaId", "seasonId");
        return Results.Ok(ApiResults.Ok(await handler.MaterialsAsync(reader.Guid("workAreaId"), reader.Guid("seasonId"), cancellationToken), httpContext));
    }
}
