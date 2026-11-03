using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Application.Purchases;
using Resource.Contracts.Purchases;

namespace Resource.Api.Endpoints;

public static class InternalPurchaseEndpoints
{
    public static void MapInternalPurchaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal").AllowAnonymous().AddEndpointFilter<InternalApiKeyFilter>().ExcludeFromDescription();
        group.MapGet("/purchase-requests/{id:guid}", async (Guid id, InternalPurchaseQueries queries, HttpContext context, CancellationToken ct) =>
            Results.Ok(ApiResults.Ok(await queries.GetAsync(id, ct), context)))
            .WithName("internalGetPurchaseRequest").Produces<ApiResponse<InternalPurchaseRequestSnapshot>>().ProducesProblem(404).ProducesProblem(503);
        group.MapGet("/purchase-requests", async (Guid? afterId, int? limit, InternalPurchaseQueries queries, HttpContext context, CancellationToken ct) =>
            Results.Ok(ApiResults.Ok(await queries.ListAsync(afterId, limit ?? 100, ct), context)))
            .WithName("internalListPurchaseRequestSnapshots").Produces<ApiResponse<InternalPurchaseRequestPage>>();
        group.MapGet("/tasks/{taskId:guid}/material-summary", async (Guid taskId, InternalPurchaseQueries queries, HttpContext context, CancellationToken ct) =>
            Results.Ok(ApiResults.Ok(await queries.MaterialsAsync(taskId, ct), context)))
            .WithName("internalGetTaskMaterialSummary").Produces<ApiResponse<InternalTaskMaterialSummary>>().ProducesProblem(404);
    }
}
