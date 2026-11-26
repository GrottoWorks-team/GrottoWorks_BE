using Microsoft.EntityFrameworkCore;
using Resource.Application.Inventory;
using Resource.Domain;
using Resource.Domain.Purchases;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Inventory;

public sealed class SelfPurchaseStore(ResourceDbContext dbContext) : ISelfPurchaseStore
{
    public void Add(SelfPurchaseReceipt receipt) => dbContext.SelfPurchaseReceipts.Add(receipt);

    public async Task<IReadOnlyList<SelfPurchaseReceipt>> ListAsync(
        Guid parishId, Guid? workAreaId, Guid? seasonId, int limit, CancellationToken cancellationToken = default)
    {
        var query = dbContext.SelfPurchaseReceipts.AsNoTracking()
            .Include(receipt => receipt.Items)
            .Where(receipt => receipt.ParishId == parishId);
        if (workAreaId is { } area)
        {
            query = query.Where(receipt => receipt.WorkAreaId == area);
        }

        if (seasonId is { } season)
        {
            query = query.Where(receipt => receipt.SeasonId == season);
        }

        return await query
            .OrderByDescending(receipt => receipt.PurchaseDate)
            .ThenByDescending(receipt => receipt.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialTransactionConfiguration.ReferenceIndex))
        {
            throw new DomainRuleException("SELF_PURCHASE_ALREADY_POSTED", "This receipt line was already posted to stock.");
        }
    }
}
