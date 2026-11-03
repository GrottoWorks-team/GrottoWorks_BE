using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using Resource.Api.Common;
using Resource.Application.Inventory;
using Resource.Contracts.Inventory;

namespace Resource.Api.Endpoints;

public static class InternalSupportReconciliationEndpoints
{
    public static void MapInternalSupportReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/internal").AllowAnonymous().AddEndpointFilter<InternalApiKeyFilter>().ExcludeFromDescription();
        group.MapGet("/support-transfer-operations/{operationId:guid}", async (Guid operationId,
            SupportTransferOperationHandler handler, HttpContext http, CancellationToken ct) =>
            await handler.GetAsync(operationId, ct) is { } result ? Results.Ok(ApiResults.Ok(result, http)) : Results.NotFound())
            .WithName("internalGetSupportTransferOperation").Produces<ApiResponse<SupportTransferOperationResult>>()
            .Produces<ProblemDetails>(401).Produces(404);
        group.MapGet("/support-transfers",async ([FromQuery] Guid[] ids,SupportReconciliationQueries queries,HttpContext http,CancellationToken ct)=>
            Results.Ok(ApiResults.Ok(await queries.TransfersAsync(ids,ct),http)))
            .WithName("internalGetSupportTransfers").Produces<ApiResponse<IReadOnlyList<SupportTransferSnapshot>>>()
            .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(503);
        group.MapGet("/work-areas/{workAreaId:guid}/available-materials",async (Guid workAreaId,[FromQuery] Guid[] materialIds,
            SupportReconciliationQueries queries,HttpContext http,CancellationToken ct)=>Results.Ok(ApiResults.Ok(await queries.AvailabilityAsync(workAreaId,materialIds,ct),http)))
            .WithName("internalGetAvailableSupportMaterials").Produces<ApiResponse<IReadOnlyList<AvailableSupportMaterial>>>()
            .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(404);
    }
}
