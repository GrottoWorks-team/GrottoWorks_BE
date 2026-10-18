using Resource.Domain.Inventory;

namespace Resource.Application.Inventory;

public sealed record QuantityByMaterial(Guid WorkAreaId, Guid MaterialId, decimal Quantity);

public sealed record TransactionQuery(
    Guid MaterialId,
    IReadOnlyCollection<Guid> WorkAreaIds,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit);

/// <summary>Stock ledger store (F-RES-04). Appends only — there is no update or delete of ledger rows.</summary>
public interface IInventoryStore
{
    void Append(MaterialTransaction transaction);

    Task<bool> OpeningStockExistsAsync(Guid workAreaId, Guid materialId, CancellationToken cancellationToken = default);

    /// <summary>SUM(transaction_quantity) per (work area, material) — the confirmed available quantity.</summary>
    Task<IReadOnlyList<QuantityByMaterial>> GetLedgerTotalsAsync(
        IReadOnlyCollection<Guid> workAreaIds,
        Guid? materialId,
        CancellationToken cancellationToken = default);

    /// <summary>Required quantity of ACTIVE requirements per (work area, material).</summary>
    Task<IReadOnlyList<QuantityByMaterial>> GetRequiredTotalsAsync(
        IReadOnlyCollection<Guid> workAreaIds,
        Guid? materialId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialTransaction>> ListTransactionsAsync(TransactionQuery query, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
