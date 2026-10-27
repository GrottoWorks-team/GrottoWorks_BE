using Microsoft.EntityFrameworkCore;
using Resource.Application.Purchases;
using Resource.Domain;
using Resource.Domain.Purchases;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Purchases;

public sealed class PurchaseStore(ResourceDbContext dbContext) : IPurchaseStore
{
    public Task<Purchase?> GetAsync(Guid purchaseId, CancellationToken cancellationToken = default) =>
        dbContext.Purchases.Include(purchase => purchase.Items).FirstOrDefaultAsync(purchase => purchase.PurchaseId == purchaseId, cancellationToken);

    public async Task<IReadOnlyList<Purchase>> ListByRequestAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default) =>
        await dbContext.Purchases.AsNoTracking()
            .Include(purchase => purchase.Items)
            .Where(purchase => purchase.PurchaseRequestId == purchaseRequestId)
            .OrderBy(purchase => purchase.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(Purchase purchase) => dbContext.Purchases.Add(purchase);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialTransactionConfiguration.ReferenceIndex))
        {
            throw new DomainRuleException("PURCHASE_ALREADY_POSTED", "This delivery was already posted to stock.");
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is Purchase or PurchaseRequest))
        {
            throw new DomainRuleException(
                "PURCHASE_VERSION_MISMATCH",
                "The purchase was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
