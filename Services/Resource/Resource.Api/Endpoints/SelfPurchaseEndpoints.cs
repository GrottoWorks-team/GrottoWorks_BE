using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Inventory;
using Resource.Domain.Purchases;

namespace Resource.Api.Endpoints;

/// <summary>
/// 26/11/2026: materials someone bought outside the purchase flow ("vật tư người khác tự mua"). The material officer
/// records them with a mandatory invoice photo; stock increases immediately and the cost is kept for money reports.
/// </summary>
public static class SelfPurchaseEndpoints
{
    private static readonly string[] Readers = [GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer];

    public static void MapSelfPurchaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/work-areas/{workAreaId:guid}/self-purchases", RecordAsync)
            .WithName("recordSelfPurchase")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<CreateSelfPurchaseReceiptRequest>("application/json")
            .Produces<ApiResponse<SelfPurchaseReceiptDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/self-purchases", ListAsync)
            .WithName("listSelfPurchases")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(Readers))
            .Produces<ApiResponse<IReadOnlyList<SelfPurchaseReceiptDto>>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // 26/11/2026: TNV tự mua khi đang làm task mà thiếu vật tư khẩn cấp (giá trị < 500.000đ).
        app.MapPost("/api/v1/tasks/{taskId:guid}/emergency-purchases", RecordEmergencyAsync)
            .WithName("recordEmergencyPurchase")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Volunteer))
            .Accepts<RecordEmergencyPurchaseRequest>("application/json")
            .Produces<ApiResponse<SelfPurchaseReceiptDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> RecordEmergencyAsync(
        Guid taskId,
        RecordEmergencyPurchaseRequest request,
        HttpContext httpContext,
        RecordEmergencyPurchaseCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var receipt = await handler.HandleAsync(
            new RecordEmergencyPurchaseCommand(
                taskId,
                request.BuyerName,
                request.SupplierName,
                request.PurchaseDate,
                request.Items!.Select(item => new SelfPurchaseLineInput(item.MaterialId!.Value, item.Quantity, item.UnitPrice)).ToList(),
                (request.Invoices ?? []).Select(file => file.Id!.Value).ToList(),
                request.Note),
            cancellationToken);
        return Results.Json(ApiResults.Ok(receipt, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> RecordAsync(
        Guid workAreaId,
        CreateSelfPurchaseReceiptRequest request,
        HttpContext httpContext,
        CreateSelfPurchaseReceiptCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var receipt = await handler.HandleAsync(
            new CreateSelfPurchaseReceiptCommand(
                workAreaId,
                request.BuyerName,
                request.SupplierName,
                request.PurchaseDate,
                request.Items!.Select(item => new SelfPurchaseLineInput(item.MaterialId!.Value, item.Quantity, item.UnitPrice)).ToList(),
                (request.Invoices ?? []).Select(file => file.Id!.Value).ToList(),
                request.Note),
            cancellationToken);
        return Results.Json(ApiResults.Ok(receipt, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        ListSelfPurchaseReceiptsQueryHandler handler,
        Guid? workAreaId,
        Guid? seasonId,
        Guid? parishId,
        int? size,
        CancellationToken cancellationToken)
    {
        var receipts = await handler.HandleAsync(
            new ListSelfPurchaseReceiptsQuery(workAreaId, seasonId, parishId, size ?? 200), cancellationToken);
        return Results.Ok(ApiResults.Ok(receipts, httpContext));
    }
}
