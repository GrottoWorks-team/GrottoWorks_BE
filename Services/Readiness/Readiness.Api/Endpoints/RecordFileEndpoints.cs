using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Readiness.Application.Files;

namespace Readiness.Api.Endpoints;

public static class RecordFileEndpoints
{
    public static void MapRecordFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/readiness-checks/{checkId:guid}/items/{itemId:guid}/evidence/{fileId:guid}/download-url", async (Guid checkId, Guid itemId, Guid fileId, ReadinessEvidenceDownload handler, HttpContext context, CancellationToken ct) =>
            Results.Ok(ApiResults.Ok(await handler.GetAsync(checkId, itemId, fileId, ct), context)))
            .WithName("getReadinessEvidenceDownloadUrl").WithTags("Readiness")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer, GrottoWorksRoles.Parish, GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<RecordFileDownload>>().ProducesProblem(404).ProducesProblem(503);
    }
}
