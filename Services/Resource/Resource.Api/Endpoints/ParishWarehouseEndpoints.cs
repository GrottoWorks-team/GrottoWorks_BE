using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Contributions;
using Resource.Application.Inventory;
using Resource.Domain.Inventory;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-21: parish warehouse balances and the two-step hand-down to work areas (tag Materials).</summary>
public static class ParishWarehouseEndpoints
{
    private static readonly string[] InventoryReaders = [GrottoWorksRoles.Parish, GrottoWorksRoles.Admin];
    private static readonly string[] DistributionReaders =
        [GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer, GrottoWorksRoles.Admin];

    public static void MapParishInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/parish-inventory/opening-stock", RecordOpeningStockAsync)
            .WithName("recordParishOpeningStock")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish))
            .Accepts<ParishOpeningStockRequest>("application/json")
            .Produces<ApiResponse<ParishInventoryTransactionDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/parish-inventory", ListAsync)
            .WithName("listParishInventory")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(InventoryReaders))
            .Produces<PagedResponse<ParishInventoryBalanceDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/v1/parish-inventory/history", HistoryAsync)
            .WithName("getParishMaterialHistory")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(InventoryReaders))
            .Produces<ApiResponse<IReadOnlyList<ParishInventoryTransactionDto>>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/parish-inventory-adjustments", AdjustAsync)
            .WithName("createParishInventoryAdjustment")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish))
            .Accepts<ParishInventoryAdjustmentRequest>("application/json")
            .Produces<ApiResponse<ParishInventoryTransactionDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        // 26/11/2026: vật tư Ban phụ trách đã MUA (không qua đơn mua/việc đi mua) — nhập thẳng vào kho tổng,
        // bắt buộc ảnh hoá đơn, sau đó phân bổ xuống khu vực đang thiếu.
        app.MapPost("/api/v1/parish-inventory/purchases", RecordPurchaseAsync)
            .WithName("recordParishPurchase")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish))
            .Accepts<RecordParishPurchaseRequest>("application/json")
            .Produces<ApiResponse<ParishPurchaseReceiptDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/parish-inventory/purchases", ListPurchasesAsync)
            .WithName("listParishPurchases")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(InventoryReaders))
            .Produces<ApiResponse<IReadOnlyList<ParishPurchaseReceiptDto>>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // PARISH records a material donation straight into the parish warehouse; the donor picker needs the parish's
        // donor list, which the material-officer donor endpoint does not serve to PARISH.
        app.MapGet("/api/v1/parish-donors", ListDonorsAsync)
            .WithName("listParishDonors")
            .WithTags("Contributions")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish))
            .Produces<PagedResponse<DonorDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    public static void MapParishDistributionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parish-distributions").WithTags("Materials");

        group.MapGet("", ListDistributionsAsync)
            .WithName("listParishDistributions")
            .RequireAuthorization(policy => policy.RequireRole(DistributionReaders))
            .Produces<PagedResponse<ParishDistributionDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{distributionId:guid}", GetDistributionAsync)
            .WithName("getParishDistribution")
            .RequireAuthorization(policy => policy.RequireRole(DistributionReaders))
            .Produces<ApiResponse<ParishDistributionDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", CreateDistributionAsync)
            .WithName("createParishDistribution")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish))
            .Accepts<CreateParishDistributionRequest>("application/json")
            .Produces<ApiResponse<ParishDistributionDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{distributionId:guid}/confirm-receipt", ConfirmReceiptAsync)
            .WithName("confirmParishDistributionReceipt")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<ConfirmParishDistributionRequest>("application/json")
            .Produces<ApiResponse<ParishDistributionDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{distributionId:guid}/cancel", CancelAsync)
            .WithName("cancelParishDistribution")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish))
            .Accepts<CancelParishDistributionRequest>("application/json")
            .Produces<ApiResponse<ParishDistributionDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> RecordOpeningStockAsync(
        ParishOpeningStockRequest request,
        HttpContext httpContext,
        RecordParishOpeningStockCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var transaction = await handler.HandleAsync(
            new RecordParishOpeningStockCommand(
                request.SeasonId!.Value,
                request.MaterialId!.Value,
                request.Quantity!.Value,
                request.TransactionDate,
                Text.Clean(request.Note)),
            cancellationToken);
        return Results.Json(ApiResults.Ok(transaction, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        ListParishInventoryQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "seasonId", "materialId", "parishId");
        var (page, size) = reader.Paging();
        var result = await handler.HandleAsync(
            new ListParishInventoryQuery(
                reader.Guid("seasonId") ?? throw MissingSeason(),
                reader.Guid("materialId"),
                page,
                size,
                reader.Guid("parishId")),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    private static async Task<IResult> HistoryAsync(
        HttpContext httpContext,
        GetParishMaterialHistoryQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "seasonId", "materialId", "from", "to", "parishId");
        var history = await handler.HandleAsync(
            new ParishMaterialHistoryQuery(
                reader.Guid("seasonId") ?? throw MissingSeason(),
                reader.Guid("materialId") ?? throw MissingMaterial(),
                reader.DateTime("from"),
                reader.DateTime("to"),
                reader.Guid("parishId")),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(history, httpContext));
    }

    private static async Task<IResult> AdjustAsync(
        ParishInventoryAdjustmentRequest request,
        HttpContext httpContext,
        CreateParishAdjustmentCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var transaction = await handler.HandleAsync(
            new CreateParishAdjustmentCommand(
                request.SeasonId!.Value,
                request.MaterialId!.Value,
                request.Type!.Value,
                request.Quantity!.Value,
                request.TransactionDate,
                request.Reason),
            cancellationToken);
        return Results.Json(ApiResults.Ok(transaction, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListDistributionsAsync(
        HttpContext httpContext,
        ListParishDistributionsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "seasonId", "workAreaId", "status", "parishId");
        var (page, size) = reader.Paging();
        var result = await handler.HandleAsync(
            new ParishDistributionFilter(page, size, reader.Guid("seasonId"), reader.Guid("workAreaId"),
                reader.Enum<ParishDistributionStatus>("status"), reader.Guid("parishId")),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    private static async Task<IResult> GetDistributionAsync(
        Guid distributionId,
        HttpContext httpContext,
        GetParishDistributionQueryHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(distributionId, cancellationToken), httpContext));

    private static async Task<IResult> CreateDistributionAsync(
        CreateParishDistributionRequest request,
        HttpContext httpContext,
        CreateParishDistributionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var distribution = await handler.HandleAsync(
            new CreateParishDistributionCommand(
                request.SeasonId!.Value,
                request.TargetWorkAreaId!.Value,
                request.Items!.Select(item => new DistributionLineInput(item.MaterialId!.Value, item.Quantity!.Value)).ToList(),
                Text.Clean(request.Note)),
            cancellationToken);
        return Results.Json(ApiResults.Ok(distribution, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ConfirmReceiptAsync(
        Guid distributionId,
        ConfirmParishDistributionRequest request,
        HttpContext httpContext,
        ConfirmParishDistributionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var distribution = await handler.HandleAsync(
            new ConfirmParishDistributionCommand(
                distributionId,
                VersionHeader.Resolve(request.Version, httpContext, "PARISH_DISTRIBUTION_VERSION_MISMATCH"),
                request.Items!.Select(item => new DistributionReceiptLineInput(
                    item.ItemId!.Value, item.UsableQuantity!.Value, item.UnusableQuantity ?? 0)).ToList(),
                Text.Clean(request.Note)),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(distribution, httpContext));
    }

    private static async Task<IResult> CancelAsync(
        Guid distributionId,
        CancelParishDistributionRequest request,
        HttpContext httpContext,
        CancelParishDistributionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var distribution = await handler.HandleAsync(
            new CancelParishDistributionCommand(
                distributionId,
                VersionHeader.Resolve(request.Version, httpContext, "PARISH_DISTRIBUTION_VERSION_MISMATCH"),
                request.Reason),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(distribution, httpContext));
    }

    private static async Task<IResult> RecordPurchaseAsync(
        RecordParishPurchaseRequest request,
        HttpContext httpContext,
        RecordParishPurchaseCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var receipt = await handler.HandleAsync(
            new RecordParishPurchaseCommand(
                request.SeasonId!.Value,
                request.BuyerName,
                request.SupplierName,
                request.PurchaseDate,
                (request.Items ?? []).Select(item => new ParishPurchaseLineInput(
                    item.MaterialId!.Value, item.Quantity, item.UnitPrice)).ToList(),
                (request.Invoices ?? []).Select(file => file.Id!.Value).ToList(),
                Text.Clean(request.Note)),
            cancellationToken);
        return Results.Json(ApiResults.Ok(receipt, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListPurchasesAsync(
        HttpContext httpContext,
        ListParishPurchasesQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "seasonId", "limit", "parishId");
        var limit = int.TryParse(reader.String("limit"), out var parsedLimit) ? parsedLimit : 50;
        var receipts = await handler.HandleAsync(
            new ListParishPurchasesQuery(
                reader.Guid("seasonId") ?? throw MissingSeason(),
                limit,
                reader.Guid("parishId")),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(receipts, httpContext));
    }

    private static async Task<IResult> ListDonorsAsync(
        HttpContext httpContext,
        ListDonorsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "q");
        var (page, size) = reader.Paging();
        var result = await handler.HandleAsync(
            new DonorFilter(page, size, null, reader.String("q"), null, DonorSortField.Name, false),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    private static Resource.Domain.DomainRuleException MissingSeason() =>
        Resource.Domain.DomainRuleException.Validation("SEASON_REQUIRED", "'seasonId' is required.");

    private static Resource.Domain.DomainRuleException MissingMaterial() =>
        Resource.Domain.DomainRuleException.Validation("MATERIAL_REQUIRED", "'materialId' is required.");
}
