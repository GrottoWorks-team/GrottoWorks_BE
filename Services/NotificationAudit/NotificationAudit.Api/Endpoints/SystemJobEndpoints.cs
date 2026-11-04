using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Contracts.SystemJobs;
using NotificationAudit.Domain.SystemJobs;

namespace NotificationAudit.Api.Endpoints;

public static class SystemJobEndpoints
{
    public static void MapSystemJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("NotificationAudit")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin));
        group.MapPost("/system-data-exports", async (SystemExportRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? key, SystemJobHandler handler, HttpContext context, CancellationToken ct) =>
        {
            var job = await handler.CreateAsync(SystemJobKind.Export, request.Services, key, Correlation(context), ct);
            return Results.Accepted(value: ApiResults.Ok(job, context));
        }).WithName("createSystemDataExport").Produces<ApiResponse<SystemJobDto>>(202)
            .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(409).Produces<ProblemDetails>(422);
        group.MapPost("/backups", async (BackupRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? key, SystemJobHandler handler, HttpContext context, CancellationToken ct) =>
        {
            var job = await handler.CreateAsync(SystemJobKind.Backup, request.Services, key, Correlation(context), ct);
            return Results.Accepted($"/api/v1/backups/{job.JobId}", ApiResults.Ok(job, context));
        }).WithName("startBackup").Produces<ApiResponse<SystemJobDto>>(202)
            .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(409).Produces<ProblemDetails>(422);
        group.MapGet("/backups/{jobId:guid}", async (Guid jobId, SystemJobHandler handler, HttpContext context, CancellationToken ct) =>
            Results.Ok(ApiResults.Ok(await handler.GetBackupAsync(jobId, ct), context)))
            .WithName("getBackupStatus").Produces<ApiResponse<SystemJobDto>>()
            .Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(404);
    }

    private static Guid? Correlation(HttpContext context) =>
        Guid.TryParse(CorrelationIdMiddlewareExtensions.GetCorrelationId(context), out var id) ? id : null;
}
