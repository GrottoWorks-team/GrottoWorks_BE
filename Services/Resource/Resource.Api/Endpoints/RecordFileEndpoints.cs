using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Application.Files;

namespace Resource.Api.Endpoints;

public static class RecordFileEndpoints
{
    public static void MapRecordFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/purchases/{purchaseId:guid}/receipts/{fileId:guid}/download-url", async (Guid purchaseId, Guid fileId, PurchaseReceiptDownload handler, HttpContext context, CancellationToken ct) =>
            Results.Ok(ApiResults.Ok(await handler.GetAsync(purchaseId, fileId, ct), context)))
            .WithName("getPurchaseReceiptDownloadUrl").WithTags("Resource")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer, GrottoWorksRoles.Parish, GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<RecordFileDownload>>().ProducesProblem(404).ProducesProblem(503);
    }
}
