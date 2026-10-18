using Resource.Application.Inventory;
using Resource.Domain.Purchases;

namespace Resource.Application.Purchases;

public sealed record PurchaseRequestSearch(
    IReadOnlyCollection<Guid> WorkAreaIds,
    PurchaseRequestStatus? Status,
    int Page,
    int Size,
    bool NewestFirst);

/// <summary>Store of the <c>purchase_request</c> aggregate (F-RES-06/07).</summary>
public interface IPurchaseRequestStore
{
    /// <summary>Tracked, with its items (not its history).</summary>
    Task<PurchaseRequest?> GetAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default);

    Task<PagedResult<PurchaseRequest>> SearchAsync(PurchaseRequestSearch search, CancellationToken cancellationToken = default);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<PurchaseRequestStateChange>> ListStateChangesAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quantity per material already asked for by other open requests of the work area
    /// (<see cref="PurchaseRequestRules.OpenStatuses"/>) — not delivered yet, so not in the ledger.
    /// </summary>
    Task<IReadOnlyList<QuantityByMaterial>> GetOpenRequestedQuantitiesAsync(
        Guid workAreaId,
        Guid excludePurchaseRequestId,
        CancellationToken cancellationToken = default);

    void Add(PurchaseRequest request);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
