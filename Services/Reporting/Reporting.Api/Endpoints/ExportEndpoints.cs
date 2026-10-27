using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Reporting.Api.Requests;
using Reporting.Application.Exports;
using Reporting.Application.Reports;
using Reporting.Domain;
using Reporting.Domain.Exports;

namespace Reporting.Api.Endpoints;

/// <summary>F-RPT-06: asynchronous report exports (tag Reports).</summary>
public static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/report-exports").WithTags("Reports")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish));
        group.MapPost("", CreateAsync)
            .WithName("createReportExport")
            .Accepts<CreateReportExportRequest>("application/json")
            .Produces<ApiResponse<ExportJobDto>>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/{jobId:guid}", GetAsync)
            .WithName("getReportExport")
            .Produces<ApiResponse<ExportJobDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{jobId:guid}/file", DownloadAsync)
            .WithName("downloadReportExport")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> CreateAsync(
        CreateReportExportRequest request,
        HttpContext httpContext,
        ReportExportHandler handler,
        CancellationToken cancellationToken)
    {
        if (request.From is { } from && request.To is { } to && to < from)
        {
            throw new DomainRuleException("INVALID_FILTER_VALUE", "'to' must be on or after 'from'.", DomainErrorKind.BadRequest);
        }

        var (job, _) = await handler.CreateAsync(
            new CreateReportExportCommand(
                request.ReportType!.Value,
                request.Format ?? ExportFormat.Xlsx,
                new ReportQuery(request.SeasonId!.Value, request.WorkAreaId, request.CommunityId, request.From, request.To),
                httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault()),
            cancellationToken);
        httpContext.Response.Headers.Location = $"/api/v1/report-exports/{job.Id}";
        return Results.Json(ApiResults.Ok(job, httpContext), statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> GetAsync(Guid jobId, HttpContext httpContext, ReportExportHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.GetAsync(jobId, cancellationToken), httpContext));

    private static async Task<IResult> DownloadAsync(Guid jobId, ReportExportHandler handler, CancellationToken cancellationToken)
    {
        var file = await handler.DownloadAsync(jobId, cancellationToken);
        return Results.File(file.Content, file.ContentType, file.FileName);
    }
}
