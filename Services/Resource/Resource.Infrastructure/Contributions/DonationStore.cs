using Microsoft.EntityFrameworkCore;
using Resource.Application;
using Resource.Application.Contributions;
using Resource.Domain;
using Resource.Domain.Contributions;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Contributions;

public sealed class DonationStore(ResourceDbContext dbContext) : IDonationStore
{
    public Task<Donation?> GetAsync(Guid donationId, CancellationToken cancellationToken = default) =>
        dbContext.Donations
            .Include(donation => donation.Items)
            .Include(donation => donation.Receipts).ThenInclude(receipt => receipt.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(donation => donation.DonationId == donationId, cancellationToken);

    public async Task<PagedResult<Donation>> SearchAsync(DonationSearch search, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Donations.AsNoTracking();
        if (search.SeasonIds is { } seasonIds)
        {
            query = query.Where(donation => seasonIds.Contains(donation.SeasonId));
        }

        if (search.WorkAreaId is { } workAreaId)
        {
            query = query.Where(donation => donation.WorkAreaId == workAreaId);
        }

        if (search.DonorId is { } donorId)
        {
            query = query.Where(donation => donation.DonorId == donorId);
        }

        if (search.Type is { } type)
        {
            query = query.Where(donation => donation.Type == type);
        }

        if (search.Status is { } status)
        {
            query = query.Where(donation => donation.Status == status);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(donation => donation.DonationDate)
            .ThenByDescending(donation => donation.DonationId)
            .Skip((search.Page - 1) * search.Size)
            .Take(search.Size)
            .Include(donation => donation.Items)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return new PagedResult<Donation>(items, total);
    }

    public async Task<IReadOnlyList<Application.Inventory.QuantityByMaterial>> GetOutstandingPledgesAsync(
        IReadOnlyCollection<Guid> workAreaIds,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Donations.AsNoTracking()
            .Where(donation => donation.WorkAreaId != null && workAreaIds.Contains(donation.WorkAreaId.Value) &&
                               (donation.Status == DonationStatus.Pledged || donation.Status == DonationStatus.PartiallyReceived))
            .SelectMany(donation => donation.Items
                .Where(item => item.PledgedQuantity != null && item.PledgedQuantity > item.ReceivedQuantity)
                .Select(item => new { WorkAreaId = donation.WorkAreaId!.Value, item.MaterialId, Outstanding = item.PledgedQuantity!.Value - item.ReceivedQuantity }))
            .GroupBy(row => new { row.WorkAreaId, row.MaterialId })
            .Select(group => new { group.Key.WorkAreaId, group.Key.MaterialId, Quantity = group.Sum(row => row.Outstanding) })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new Application.Inventory.QuantityByMaterial(row.WorkAreaId, row.MaterialId, row.Quantity)).ToList();
    }

    public void Add(Donation donation) => dbContext.Donations.Add(donation);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialTransactionConfiguration.ReferenceIndex))
        {
            throw new DomainRuleException("DONATION_RECEIPT_ALREADY_POSTED", "This receipt was already posted to stock.");
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is Donation))
        {
            throw new DomainRuleException(
                "DONATION_VERSION_MISMATCH",
                "The donation was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
