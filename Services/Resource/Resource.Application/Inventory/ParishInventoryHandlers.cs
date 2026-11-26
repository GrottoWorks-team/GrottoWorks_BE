using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Application.Inventory;

public sealed record RecordParishOpeningStockCommand(
    Guid SeasonId,
    Guid MaterialId,
    decimal Quantity,
    DateTimeOffset? TransactionDate,
    string? Note);

public sealed record ListParishInventoryQuery(
    Guid SeasonId,
    Guid? MaterialId,
    int Page,
    int Size,
    Guid? ParishId = null);

public sealed record ParishMaterialHistoryQuery(
    Guid SeasonId,
    Guid MaterialId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ParishId = null);

public sealed record CreateParishAdjustmentCommand(
    Guid SeasonId,
    Guid MaterialId,
    AdjustmentKind Kind,
    decimal Quantity,
    DateTimeOffset? TransactionDate,
    string? Reason);

/// <summary>
/// F-RES-21 <c>recordParishOpeningStock</c> (PARISH): the usable stock the parish warehouse starts a season with.
/// Posted once per (parish, season, material); later corrections go through a parish inventory adjustment.
/// </summary>
public sealed class RecordParishOpeningStockCommandHandler(
    ParishWarehouseAccess access,
    IMaterialCatalogStore catalog,
    IParishInventoryStore inventory,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public const int MaxNoteLength = 2000;

    public Task<ParishInventoryTransactionDto> HandleAsync(
        RecordParishOpeningStockCommand command,
        CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(() => HandleInTransactionAsync(command, cancellationToken), cancellationToken);

    private async Task<ParishInventoryTransactionDto> HandleInTransactionAsync(
        RecordParishOpeningStockCommand command,
        CancellationToken cancellationToken)
    {
        var (_, parishId) = await access.RequireWritableSeasonAsync(command.SeasonId, cancellationToken);
        var material = await catalog.GetMaterialAsync(command.MaterialId, cancellationToken)
            ?? throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");
        if (material.Status != CatalogStatus.Active)
        {
            throw DomainRuleException.Validation("MATERIAL_INACTIVE", "The material is inactive.");
        }

        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        if (note is { Length: > MaxNoteLength })
        {
            throw DomainRuleException.Validation("PARISH_OPENING_STOCK_NOTE_TOO_LONG", $"Note must not exceed {MaxNoteLength} characters.");
        }

        var now = DateTimeOffset.UtcNow;
        var transactionDate = command.TransactionDate ?? now;
        if (transactionDate > now.AddMinutes(5))
        {
            throw DomainRuleException.Validation("TRANSACTION_DATE_IN_FUTURE", "Opening stock cannot be dated in the future.");
        }

        if (await inventory.OpeningStockExistsAsync(parishId, command.SeasonId, command.MaterialId, cancellationToken))
        {
            throw new DomainRuleException(
                "PARISH_OPENING_STOCK_ALREADY_RECORDED",
                "Opening stock was already recorded for this material in this season. Use a parish inventory adjustment.");
        }

        var transaction = ParishMaterialTransaction.Post(
            parishId, command.SeasonId, command.MaterialId, TransactionType.OpeningStock, command.Quantity, transactionDate,
            currentUser.UserId, note);
        inventory.Append(transaction);
        await inventory.SaveChangesAsync(cancellationToken);
        await PublishStockAsync(parishId, command.SeasonId, material, cancellationToken);
        return ParishInventoryTransactionDto.From(transaction);
    }

    internal async Task PublishStockAsync(Guid parishId, Guid seasonId, Material material, CancellationToken cancellationToken)
    {
        var available = Math.Max(0, (await inventory.GetTotalsAsync(parishId, seasonId, material.MaterialId, cancellationToken))
            .Sum(row => row.Quantity));
        await publisher.PublishAsync(
            ParishMaterialStockChanged.EventType,
            ParishMaterialStockChanged.Version,
            new ParishMaterialStockChanged(parishId, seasonId, material.MaterialId, available, DateTimeOffset.UtcNow,
                material.Code, material.Name, material.Unit),
            "parish_material",
            material.MaterialId,
            cancellationToken: cancellationToken);
    }
}

/// <summary>F-RES-21 <c>listParishInventory</c> (PARISH; ADMIN names the parish): balances of one season.</summary>
public sealed class ListParishInventoryQueryHandler(
    ParishWarehouseAccess access,
    IParishInventoryStore inventory,
    IMaterialCatalogStore catalog)
{
    public async Task<PagedResult<ParishInventoryBalanceDto>> HandleAsync(
        ListParishInventoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var parishId = access.RequireReadParish(query.ParishId);
        await access.RequireReadableSeasonAsync(query.SeasonId, parishId, cancellationToken);

        var totals = await inventory.GetTotalsAsync(parishId, query.SeasonId, query.MaterialId, cancellationToken);
        var materials = (await catalog.GetMaterialsAsync(totals.Select(row => row.MaterialId).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);

        var rows = totals
            .Where(row => materials.ContainsKey(row.MaterialId))
            .Select(row =>
            {
                var material = materials[row.MaterialId];
                return new ParishInventoryBalanceDto(material.MaterialId, material.Code, material.Name, material.Unit,
                    Math.Max(0, row.Quantity));
            })
            .OrderBy(row => row.MaterialName, StringComparer.CurrentCulture)
            .ThenBy(row => row.MaterialId)
            .ToList();

        return new PagedResult<ParishInventoryBalanceDto>(
            rows.Skip((query.Page - 1) * query.Size).Take(query.Size).ToList(),
            rows.Count);
    }
}

/// <summary>F-RES-21 <c>getParishMaterialHistory</c>: ledger rows of one material in the parish warehouse.</summary>
public sealed class GetParishMaterialHistoryQueryHandler(
    ParishWarehouseAccess access,
    IParishInventoryStore inventory,
    IMaterialCatalogStore catalog)
{
    public const int MaxRows = 500;

    public async Task<IReadOnlyList<ParishInventoryTransactionDto>> HandleAsync(
        ParishMaterialHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new DomainRuleException("INVALID_DATE_RANGE", "'from' must not be after 'to'.", DomainErrorKind.BadRequest);
        }

        var parishId = access.RequireReadParish(query.ParishId);
        await access.RequireReadableSeasonAsync(query.SeasonId, parishId, cancellationToken);
        _ = await catalog.GetMaterialAsync(query.MaterialId, cancellationToken)
            ?? throw DomainRuleException.NotFound("MATERIAL_NOT_FOUND", "The material does not exist.");

        var transactions = await inventory.ListTransactionsAsync(
            new ParishTransactionQuery(parishId, query.SeasonId, query.MaterialId, query.From, query.To, MaxRows),
            cancellationToken);
        return transactions.Select(ParishInventoryTransactionDto.From).ToList();
    }
}

/// <summary>
/// F-RES-21 <c>createParishInventoryAdjustment</c> (PARISH): damaged, lost or disposed stock leaves the warehouse.
/// The quantity must fit the current balance under the parish stock lock.
/// </summary>
public sealed class CreateParishAdjustmentCommandHandler(
    ParishWarehouseAccess access,
    IMaterialCatalogStore catalog,
    IParishInventoryStore inventory,
    IParishStockLock stockLock,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public const int MaxReasonLength = 500;

    public Task<ParishInventoryTransactionDto> HandleAsync(
        CreateParishAdjustmentCommand command,
        CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(() => HandleInTransactionAsync(command, cancellationToken), cancellationToken);

    private async Task<ParishInventoryTransactionDto> HandleInTransactionAsync(
        CreateParishAdjustmentCommand command,
        CancellationToken cancellationToken)
    {
        var (_, parishId) = await access.RequireWritableSeasonAsync(command.SeasonId, cancellationToken);
        var material = await catalog.GetMaterialAsync(command.MaterialId, cancellationToken)
            ?? throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        if (reason is null || reason.Length > MaxReasonLength)
        {
            throw DomainRuleException.Validation("ADJUSTMENT_REASON_REQUIRED", $"A reason of at most {MaxReasonLength} characters is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var date = command.TransactionDate ?? now;
        if (date > now.AddMinutes(5))
        {
            throw DomainRuleException.Validation("TRANSACTION_DATE_IN_FUTURE", "An adjustment cannot be dated in the future.");
        }

        var transaction = ParishMaterialTransaction.Post(
            parishId, command.SeasonId, material.MaterialId, ToType(command.Kind), command.Quantity, date, currentUser.UserId, reason,
            TransactionReferenceType.InventoryAdjustment, Guid.NewGuid());

        await using (var lease = await stockLock.AcquireAsync(parishId, command.SeasonId, cancellationToken))
        {
            var available = (await inventory.GetTotalsAsync(parishId, command.SeasonId, material.MaterialId, cancellationToken))
                .Sum(row => row.Quantity);
            if (command.Quantity > available)
            {
                throw new DomainRuleException(
                    "PARISH_STOCK_INSUFFICIENT",
                    $"Only {available} {material.Unit} of {material.Code} is available in the parish warehouse; {command.Quantity} was requested.");
            }

            inventory.Append(transaction);
            await inventory.SaveChangesAsync(cancellationToken);
            await lease.CommitAsync(cancellationToken);
        }

        var remaining = Math.Max(0, (await inventory.GetTotalsAsync(parishId, command.SeasonId, material.MaterialId, cancellationToken))
            .Sum(row => row.Quantity));
        await publisher.PublishAsync(
            ParishMaterialStockChanged.EventType,
            ParishMaterialStockChanged.Version,
            new ParishMaterialStockChanged(parishId, command.SeasonId, material.MaterialId, remaining, DateTimeOffset.UtcNow,
                material.Code, material.Name, material.Unit),
            "parish_material",
            material.MaterialId,
            cancellationToken: cancellationToken);
        return ParishInventoryTransactionDto.From(transaction);
    }

    private static TransactionType ToType(AdjustmentKind kind) => kind switch
    {
        AdjustmentKind.Damaged => TransactionType.AdjustmentDamaged,
        AdjustmentKind.Lost => TransactionType.AdjustmentLost,
        _ => TransactionType.AdjustmentDisposed
    };
}
