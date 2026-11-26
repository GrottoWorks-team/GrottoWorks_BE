using Microsoft.EntityFrameworkCore;
using Resource.Application.Inventory;
using Resource.Domain.Inventory;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Inventory;

public sealed class ParishPurchaseStore(ResourceDbContext dbContext) : IParishPurchaseStore
{
    public void Add(ParishPurchaseReceipt receipt) => dbContext.ParishPurchaseReceipts.Add(receipt);

    public async Task<IReadOnlyList<ParishPurchaseReceipt>> ListAsync(
        Guid parishId, Guid seasonId, int limit, CancellationToken cancellationToken = default) =>
        await dbContext.ParishPurchaseReceipts.AsNoTracking()
            .Include(receipt => receipt.Items)
            .Where(receipt => receipt.ParishId == parishId && receipt.SeasonId == seasonId)
            .OrderByDescending(receipt => receipt.PurchaseDate)
            .ThenByDescending(receipt => receipt.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
