using Resource.Domain.Inventory;

namespace Resource.Application.Inventory;

/// <summary>Store for parish purchase receipts (materials the parish committee bought for the central warehouse).</summary>
public interface IParishPurchaseStore
{
    void Add(ParishPurchaseReceipt receipt);

    Task<IReadOnlyList<ParishPurchaseReceipt>> ListAsync(
        Guid parishId,
        Guid seasonId,
        int limit,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
