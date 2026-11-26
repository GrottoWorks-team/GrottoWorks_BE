using Resource.Application.Abstractions;
using Resource.Domain.Inventory;

namespace Resource.Application.Inventory;

public sealed record ParishQuantityByMaterial(Guid MaterialId, decimal Quantity);

/// <summary>One parish warehouse balance (confirmed available) per material and season.</summary>
public sealed record ParishInventoryBalanceDto(
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal ConfirmedAvailable);

/// <summary>One parish warehouse ledger row as returned to clients.</summary>
public sealed record ParishInventoryTransactionDto(
    Guid Id,
    Guid MaterialId,
    TransactionType TransactionType,
    decimal Quantity,
    DateTimeOffset TransactionDate,
    TransactionReferenceType? ReferenceType,
    Guid? ReferenceId,
    Guid? CreatedByUserId,
    string? Note)
{
    public static ParishInventoryTransactionDto From(ParishMaterialTransaction transaction) =>
        new(transaction.ParishMaterialTransactionId, transaction.MaterialId, transaction.Type, transaction.Quantity,
            transaction.TransactionDate, transaction.ReferenceType, transaction.ReferenceId, transaction.CreatedByUserId, transaction.Note);
}

public sealed record ParishDistributionItemDto(
    Guid Id,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal IssuedQuantity,
    decimal? ReceivedUsableQuantity,
    decimal? ReceivedUnusableQuantity);

/// <summary><c>ParishDistribution</c>: a two-step hand-down from the parish warehouse to a work area.</summary>
public sealed record ParishDistributionDto(
    Guid Id,
    Guid ParishId,
    Guid SeasonId,
    Guid TargetWorkAreaId,
    string TargetWorkAreaName,
    ParishDistributionStatus Status,
    ParishDistributionOrigin Origin,
    Guid IssuedBy,
    DateTimeOffset IssuedAt,
    Guid? ReceivedConfirmedBy,
    DateTimeOffset? ReceivedConfirmedAt,
    string? Note,
    string? CancelReason,
    IReadOnlyList<ParishDistributionItemDto> Items,
    long Version);

public sealed record ParishTransactionQuery(
    Guid ParishId,
    Guid SeasonId,
    Guid MaterialId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit);

public sealed record ParishDistributionSearch(
    Guid ParishId,
    Guid? SeasonId,
    IReadOnlyCollection<Guid>? WorkAreaIds,
    ParishDistributionStatus? Status,
    int Page,
    int Size);

/// <summary>Append-only parish warehouse ledger store (F-RES-21).</summary>
public interface IParishInventoryStore
{
    void Append(ParishMaterialTransaction transaction);

    Task<bool> OpeningStockExistsAsync(Guid parishId, Guid seasonId, Guid materialId, CancellationToken cancellationToken = default);

    /// <summary>SUM(transaction_quantity) per material of one parish season.</summary>
    Task<IReadOnlyList<ParishQuantityByMaterial>> GetTotalsAsync(
        Guid parishId,
        Guid seasonId,
        Guid? materialId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ParishMaterialTransaction>> ListTransactionsAsync(
        ParishTransactionQuery query,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IParishDistributionStore
{
    /// <summary>Runs the operation inside one transaction (status change + ledger rows commit together).</summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>Tracked, items included.</summary>
    Task<ParishDistribution?> GetAsync(Guid distributionId, CancellationToken cancellationToken = default);

    Task<PagedResult<ParishDistribution>> SearchAsync(ParishDistributionSearch search, CancellationToken cancellationToken = default);

    void Add(ParishDistribution distribution);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Serialises parish warehouse outflows of one season (advisory transaction lock).</summary>
public interface IParishStockLock
{
    Task<IStockLease> AcquireAsync(Guid parishId, Guid seasonId, CancellationToken cancellationToken = default);
}
