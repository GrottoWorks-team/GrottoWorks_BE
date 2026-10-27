using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Inventory;
using Resource.Domain;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-04/15/16: stock ledger — opening stock, balances, allocations, adjustments, material history (tag Materials).</summary>
public static class InventoryEndpoints
{
    private static readonly string[] Readers = [GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer];

    public static void MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/inventory/opening-stock", RecordOpeningStockAsync)
            .WithName("recordOpeningStock")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<OpeningStockRequest>("application/json")
            .Produces<ApiResponse<InventoryTransactionDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/inventory", ListInventoryAsync)
            .WithName("listInventory")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(Readers))
            .Produces<PagedResponse<InventoryBalanceDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/api/v1/material-allocations", AllocateAsync)
            .WithName("allocateMaterial")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<CreateAllocationRequest>("application/json")
            .Produces<ApiResponse<MaterialAllocationDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/material-allocations", ListAllocationsAsync)
            .WithName("listMaterialAllocations")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(Readers))
            .Produces<PagedResponse<MaterialAllocationDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/v1/inventory-adjustments", AdjustAsync)
            .WithName("createInventoryAdjustment")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<InventoryAdjustmentRequest>("application/json")
            .Produces<ApiResponse<InventoryTransactionDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/materials/{materialId:guid}/allocation-history", GetHistoryAsync)
            .WithName("getAllocationHistory")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(Readers))
            .Produces<ApiResponse<IReadOnlyList<InventoryTransactionDto>>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> RecordOpeningStockAsync(
        OpeningStockRequest request,
        HttpContext httpContext,
        RecordOpeningStockCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var transaction = await handler.HandleAsync(
            new RecordOpeningStockCommand(
                request.WorkAreaId!.Value,
                request.MaterialId!.Value,
                request.Quantity!.Value,
                request.TransactionDate,
                Text.Clean(request.Note)),
            cancellationToken);
        return Results.Json(ApiResults.Ok(transaction, httpContext), statusCode: StatusCodes.Status201Created);
    }

    /// <summary><c>InventoryFilter</c>: seasonId, workAreaId, materialId, onlyShortage; paged.</summary>
    private static async Task<IResult> ListInventoryAsync(
        HttpContext httpContext,
        ListInventoryQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "seasonId", "workAreaId", "materialId", "onlyShortage");
        var (page, size) = reader.Paging();
        var onlyShortage = reader.String("onlyShortage") switch
        {
            null => false,
            var value when bool.TryParse(value, out var parsed) => parsed,
            _ => throw new DomainRuleException("INVALID_FILTER_VALUE", "'onlyShortage' must be true or false.", DomainErrorKind.BadRequest)
        };

        var result = await handler.HandleAsync(
            new InventoryFilter(page, size, reader.Guid("seasonId"), reader.Guid("workAreaId"), reader.Guid("materialId"), onlyShortage),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    private static async Task<IResult> AllocateAsync(
        CreateAllocationRequest request,
        HttpContext httpContext,
        AllocateMaterialCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var allocation = await handler.HandleAsync(
            new AllocateMaterialCommand(
                request.WorkAreaId!.Value,
                request.TaskId,
                request.Items!.Select(item => new Domain.Inventory.AllocationLineInput(item.MaterialId!.Value, item.Quantity!.Value)).ToList(),
                request.AllocationDate,
                Text.Clean(request.Note)),
            cancellationToken);
        return Results.Json(ApiResults.Ok(allocation, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> AdjustAsync(
        InventoryAdjustmentRequest request,
        HttpContext httpContext,
        CreateInventoryAdjustmentCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var transaction = await handler.HandleAsync(
            new CreateInventoryAdjustmentCommand(request.WorkAreaId!.Value, request.MaterialId!.Value, request.Type!.Value,
                request.Quantity!.Value, request.TransactionDate, request.Reason),
            cancellationToken);
        return Results.Json(ApiResults.Ok(transaction, httpContext), statusCode: StatusCodes.Status201Created);
    }

    /// <summary><c>AllocationFilter</c>: seasonId, workAreaId, taskId, materialId; newest first; paged.</summary>
    private static async Task<IResult> ListAllocationsAsync(
        HttpContext httpContext,
        ListMaterialAllocationsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "seasonId", "workAreaId", "taskId", "materialId");
        var (page, size) = reader.Paging();
        var result = await handler.HandleAsync(
            new AllocationFilter(page, size, reader.Guid("seasonId"), reader.Guid("workAreaId"), reader.Guid("taskId"), reader.Guid("materialId")),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    /// <summary><c>DateRangeQuery</c>: from, to (ISO 8601) and optional workAreaId; newest first, at most 500 rows.</summary>
    private static async Task<IResult> GetHistoryAsync(
        Guid materialId,
        HttpContext httpContext,
        GetMaterialHistoryQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "from", "to", "workAreaId");
        var history = await handler.HandleAsync(
            new MaterialHistoryQuery(materialId, reader.Guid("workAreaId"), reader.DateTime("from"), reader.DateTime("to")),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(history, httpContext));
    }
}
