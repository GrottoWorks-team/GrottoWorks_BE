using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Purchases;
using Resource.Domain.Purchases;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-09/10/11: purchases by the buyer, delivery confirmation and cost overrun (tag Purchases).</summary>
public static class PurchaseEndpoints
{
    private const string VersionMismatch = "PURCHASE_VERSION_MISMATCH";

    public static void MapPurchaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/purchase-requests/{requestId:guid}/purchases", RecordAsync)
            .WithName("recordPurchase")
            .WithTags("Purchases")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Volunteer))
            .Accepts<CreatePurchaseBody>("application/json")
            .Produces<ApiResponse<PurchaseDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        var group = app.MapGroup("/api/v1/purchases/{purchaseId:guid}").WithTags("Purchases");
        group.MapPost("/receipts", AttachReceiptsAsync)
            .WithName("attachPurchaseReceipt")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Volunteer))
            .Accepts<List<FileReferenceRequest>>("application/json")
            .Produces<ApiResponse<PurchaseDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/confirm-delivery", ConfirmDeliveryAsync)
            .WithName("confirmPurchaseDelivery")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<ConfirmDeliveryRequest>("application/json")
            .Produces<ApiResponse<PurchaseDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/request-info", RequestInfoAsync)
            .WithName("requestPurchaseInfo")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<ReasonCommandRequest>("application/json")
            .Produces<ApiResponse<PurchaseDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/approve-cost-overrun", ApproveCostOverrunAsync)
            .WithName("approveCostOverrun")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader))
            .Accepts<ApprovalCommandRequest>("application/json")
            .Produces<ApiResponse<PurchaseDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
    }

    private static async Task<IResult> RecordAsync(
        Guid requestId,
        CreatePurchaseBody request,
        HttpContext httpContext,
        RecordPurchaseCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var purchase = await handler.HandleAsync(
            new RecordPurchaseCommand(
                requestId,
                request.SupplierName,
                request.PurchaseDate,
                request.Items!.Select(item => new PurchaseLineInput(item.PurchaseRequestItemId!.Value, item.PurchasedQuantity!.Value,
                    item.UnitPrice!.Value, item.TotalPrice)).ToList(),
                (request.Receipts ?? []).Select(file => file.Id!.Value).ToList()),
            cancellationToken);
        return Results.Json(ApiResults.Ok(purchase, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> AttachReceiptsAsync(
        Guid purchaseId,
        List<FileReferenceRequest> files,
        HttpContext httpContext,
        AttachPurchaseReceiptsCommandHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.HandleAsync(new AttachPurchaseReceiptsCommand(purchaseId, files.Select(file => file.Id!.Value).ToList()), cancellationToken),
            httpContext));

    private static async Task<IResult> ConfirmDeliveryAsync(
        Guid purchaseId,
        ConfirmDeliveryRequest request,
        HttpContext httpContext,
        ConfirmPurchaseDeliveryCommandHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.HandleAsync(
                new ConfirmDeliveryCommand(
                    purchaseId,
                    VersionHeader.Resolve(request.Version, httpContext, VersionMismatch),
                    request.Items!.Select(item => new DeliveryLineInput(item.PurchaseItemId!.Value, item.ReceivedUsableQuantity!.Value,
                        item.ReceivedUnusableQuantity ?? 0)).ToList()),
                cancellationToken),
            httpContext));

    private static async Task<IResult> RequestInfoAsync(
        Guid purchaseId,
        ReasonCommandRequest request,
        HttpContext httpContext,
        PurchaseReviewCommandHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.RequestInfoAsync(
                new PurchaseDecisionCommand(purchaseId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.Reason), cancellationToken),
            httpContext));

    private static async Task<IResult> ApproveCostOverrunAsync(
        Guid purchaseId,
        ApprovalCommandRequest request,
        HttpContext httpContext,
        PurchaseReviewCommandHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.ApproveCostOverrunAsync(
                new PurchaseDecisionCommand(purchaseId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.Note), cancellationToken),
            httpContext));
}
