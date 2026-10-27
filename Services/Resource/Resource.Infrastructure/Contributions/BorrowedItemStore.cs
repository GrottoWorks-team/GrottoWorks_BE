using Microsoft.EntityFrameworkCore;
using Resource.Application;
using Resource.Application.Contributions;
using Resource.Domain;
using Resource.Domain.Contributions;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Contributions;

public sealed class BorrowedItemStore(ResourceDbContext dbContext) : IBorrowedItemStore
{
    public Task<BorrowedItem?> GetAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        dbContext.BorrowedItems.Include(item => item.Transactions).FirstOrDefaultAsync(item => item.BorrowedItemId == itemId, cancellationToken);

    public async Task<PagedResult<BorrowedItem>> SearchAsync(BorrowedItemSearch search, CancellationToken cancellationToken = default)
    {
        var statuses = search.Statuses.ToList();
        var query = dbContext.BorrowedItems.AsNoTracking()
            .Where(item => search.SeasonIds.Contains(item.SeasonId) && statuses.Contains(item.Status));
        if (search.WorkAreaId is { } workAreaId)
        {
            query = query.Where(item => item.WorkAreaId == workAreaId);
        }

        if (search.DueOnOrBefore is { } due)
        {
            query = query.Where(item => item.ExpectedReturnDate <= due);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.ExpectedReturnDate)
            .ThenBy(item => item.BorrowedItemId)
            .Skip((search.Page - 1) * search.Size)
            .Take(search.Size)
            .Include(item => item.Transactions)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return new PagedResult<BorrowedItem>(items, total);
    }

    public async Task<IReadOnlyList<BorrowedItem>> ListToRemindAsync(DateOnly dueOnOrBefore, DateOnly today, CancellationToken cancellationToken = default) =>
        await dbContext.BorrowedItems
            .Where(item => (item.Status == BorrowedItemStatus.Issued || item.Status == BorrowedItemStatus.InUse) &&
                           item.ExpectedReturnDate <= dueOnOrBefore &&
                           (item.LastRemindedOn == null || item.LastRemindedOn < today))
            .OrderBy(item => item.ExpectedReturnDate)
            .Take(500)
            .ToListAsync(cancellationToken);

    public void Add(BorrowedItem item) => dbContext.BorrowedItems.Add(item);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is BorrowedItem))
        {
            throw new DomainRuleException(
                "BORROWED_ITEM_VERSION_MISMATCH",
                "The item was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
