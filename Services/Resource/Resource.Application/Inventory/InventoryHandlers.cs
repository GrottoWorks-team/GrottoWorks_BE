using BuildingBlocks.Security;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Application.Inventory;

public sealed record RecordOpeningStockCommand(
    Guid WorkAreaId,
    Guid MaterialId,
    decimal Quantity,
    DateTimeOffset? TransactionDate,
    string? Note);

public sealed record InventoryFilter(
    int Page,
    int Size,
    Guid? SeasonId = null,
    Guid? WorkAreaId = null,
    Guid? MaterialId = null,
    bool OnlyShortage = false);

public sealed record MaterialHistoryQuery(Guid MaterialId, Guid? WorkAreaId, DateTimeOffset? From, DateTimeOffset? To);

/// <summary>
/// F-RES-04 <c>recordOpeningStock</c> (MO of the work area): the confirmed usable stock a work area starts with.
/// Posted once per (work area, material) as an OPENING_STOCK ledger row; later corrections go through
/// inventory adjustments (F-RES-16), never by editing the ledger.
/// </summary>
public sealed class RecordOpeningStockCommandHandler(
    WorkAreaAccess access,
    ICurrentUser currentUser,
    IInventoryStore inventory,
    IMaterialCatalogStore catalog,
    ShortageTracker shortages)
{
    public async Task<InventoryTransactionDto> HandleAsync(
        RecordOpeningStockCommand command,
        CancellationToken cancellationToken = default)
    {
        var scope = await access.ForMaterialOfficerWriteAsync(command.WorkAreaId, cancellationToken);

        var material = await catalog.GetMaterialAsync(command.MaterialId, cancellationToken)
            ?? throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");
        if (material.Status != CatalogStatus.Active)
        {
            throw DomainRuleException.Validation("MATERIAL_INACTIVE", "The material is inactive.");
        }

        var now = DateTimeOffset.UtcNow;
        var transactionDate = command.TransactionDate ?? now;
        if (transactionDate > now.AddMinutes(5))
        {
            throw DomainRuleException.Validation("TRANSACTION_DATE_IN_FUTURE", "Opening stock cannot be dated in the future.");
        }

        var transaction = MaterialTransaction.Post(
            command.WorkAreaId,
            command.MaterialId,
            TransactionType.OpeningStock,
            command.Quantity,
            transactionDate,
            currentUser.UserId,
            command.Note);

        if (await inventory.OpeningStockExistsAsync(command.WorkAreaId, command.MaterialId, cancellationToken))
        {
            throw new DomainRuleException(
                "OPENING_STOCK_ALREADY_RECORDED",
                "Opening stock was already recorded for this material in this work area. Use an inventory adjustment.");
        }

        var before = await shortages.CaptureAsync(scope, command.MaterialId, cancellationToken);
        inventory.Append(transaction);
        await inventory.SaveChangesAsync(cancellationToken);
        await shortages.PublishIfChangedAsync(before, cancellationToken);
        return InventoryTransactionDto.From(transaction);
    }
}

/// <summary>
/// F-RES-04 <c>listInventory</c> (PARISH, LEADER, MO): confirmed balances of the caller's parish, per work area
/// and material (BR-24 — stock is never pooled across work areas).
/// </summary>
public sealed class ListInventoryQueryHandler(WorkAreaAccess access, IWorkAreaScopeStore scopes, InventoryReader reader)
{
    public async Task<PagedResult<InventoryBalanceDto>> HandleAsync(
        InventoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var parishId = access.RequireParish();

        var areas = filter.WorkAreaId is { } workAreaId
            ? [await access.ForReadAsync(workAreaId, cancellationToken)]
            : await scopes.ListByParishAsync(parishId, filter.SeasonId, cancellationToken);

        if (filter.SeasonId is { } seasonId)
        {
            areas = areas.Where(area => area.SeasonId == seasonId).ToList();
        }

        var rows = await reader.ReadAsync(areas, filter.MaterialId, cancellationToken);
        if (filter.OnlyShortage)
        {
            rows = rows.Where(row => row.Shortage > 0).ToList();
        }

        return new PagedResult<InventoryBalanceDto>(
            rows.Skip((filter.Page - 1) * filter.Size).Take(filter.Size).ToList(),
            rows.Count);
    }
}

/// <summary>
/// F-RES-04 <c>getAllocationHistory</c> (PARISH, LEADER, MO): ledger movements of one material in the caller's
/// parish, newest first, optionally per work area and date range. Allocation rows join this history in F-RES-15.
/// </summary>
public sealed class GetMaterialHistoryQueryHandler(
    WorkAreaAccess access,
    IWorkAreaScopeStore scopes,
    IInventoryStore inventory,
    IMaterialCatalogStore catalog)
{
    public const int MaxRows = 500;

    public async Task<IReadOnlyList<InventoryTransactionDto>> HandleAsync(
        MaterialHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new DomainRuleException("INVALID_DATE_RANGE", "'from' must not be after 'to'.", DomainErrorKind.BadRequest);
        }

        _ = await catalog.GetMaterialAsync(query.MaterialId, cancellationToken)
            ?? throw DomainRuleException.NotFound("MATERIAL_NOT_FOUND", "The material does not exist.");

        var workAreaIds = query.WorkAreaId is { } workAreaId
            ? [(await access.ForReadAsync(workAreaId, cancellationToken)).WorkAreaId]
            : (await scopes.ListByParishAsync(access.RequireParish(), null, cancellationToken)).Select(scope => scope.WorkAreaId).ToList();

        if (workAreaIds.Count == 0)
        {
            return [];
        }

        var transactions = await inventory.ListTransactionsAsync(
            new TransactionQuery(query.MaterialId, workAreaIds, query.From, query.To, MaxRows),
            cancellationToken);

        return transactions.Select(InventoryTransactionDto.From).ToList();
    }
}
