using Resource.Domain.Contributions;

namespace Resource.Application.Contributions;

public sealed record DonationSearch(
    IReadOnlyCollection<Guid>? SeasonIds,
    Guid? WorkAreaId,
    Guid? DonorId,
    DonationType? Type,
    DonationStatus? Status,
    int Page,
    int Size);

/// <summary>Store of the <c>donation</c> aggregate (items, receipts and receipt lines).</summary>
public interface IDonationStore
{
    /// <summary>Tracked, with items and receipts (and their lines).</summary>
    Task<Donation?> GetAsync(Guid donationId, CancellationToken cancellationToken = default);

    /// <summary>Newest donation date first; items included, receipts not.</summary>
    Task<PagedResult<Donation>> SearchAsync(DonationSearch search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pledged but not yet received quantity per (work area, material) of PLEDGED / PARTIALLY_RECEIVED MATERIAL
    /// donations — shown next to the shortage so volunteers do not over-pledge.
    /// </summary>
    Task<IReadOnlyList<Inventory.QuantityByMaterial>> GetOutstandingPledgesAsync(
        IReadOnlyCollection<Guid> workAreaIds,
        CancellationToken cancellationToken = default);

    void Add(Donation donation);

    /// <summary>Saves the unit of work — together with ledger rows appended to the same context (one transaction).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
