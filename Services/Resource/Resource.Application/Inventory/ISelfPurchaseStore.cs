using Resource.Domain.Purchases;

namespace Resource.Application.Inventory;

/// <summary>Store for self-purchase receipts (materials bought outside the purchase flow).</summary>
public interface ISelfPurchaseStore
{
    void Add(SelfPurchaseReceipt receipt);

    Task<IReadOnlyList<SelfPurchaseReceipt>> ListAsync(
        Guid parishId,
        Guid? workAreaId,
        Guid? seasonId,
        int limit,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
