using Microsoft.EntityFrameworkCore;
using Resource.Application;
using Resource.Application.Inventory;
using Resource.Application.Purchases;
using Resource.Domain;
using Resource.Domain.Purchases;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Purchases;

public sealed class PurchaseRequestStore(ResourceDbContext dbContext) : IPurchaseRequestStore
{
    public Task<PurchaseRequest?> GetAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default) =>
        dbContext.PurchaseRequests.Include(request => request.Items)
            .FirstOrDefaultAsync(request => request.PurchaseRequestId == purchaseRequestId, cancellationToken);

    public async Task<PagedResult<PurchaseRequest>> SearchAsync(PurchaseRequestSearch search, CancellationToken cancellationToken = default)
    {
        var query = dbContext.PurchaseRequests.AsNoTracking().Where(request => search.WorkAreaIds.Contains(request.WorkAreaId));
        if (search.Status is { } status)
        {
            query = query.Where(request => request.Status == status);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var ordered = search.NewestFirst
            ? query.OrderByDescending(request => request.CreatedAt).ThenByDescending(request => request.PurchaseRequestId)
            : query.OrderBy(request => request.CreatedAt).ThenBy(request => request.PurchaseRequestId);
        var items = await ordered
            .Skip((search.Page - 1) * search.Size)
            .Take(search.Size)
            .Include(request => request.Items)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return new PagedResult<PurchaseRequest>(items, total);
    }

    public async Task<IReadOnlyList<PurchaseRequestStateChange>> ListStateChangesAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default) =>
        await dbContext.PurchaseRequestStateChanges.AsNoTracking()
            .Where(change => change.PurchaseRequestId == purchaseRequestId)
            .OrderBy(change => change.ChangedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QuantityByMaterial>> GetOpenRequestedQuantitiesAsync(
        Guid workAreaId,
        Guid excludePurchaseRequestId,
        CancellationToken cancellationToken = default)
    {
        var open = PurchaseRequestRules.OpenStatuses.ToList();
        return await dbContext.PurchaseRequests.AsNoTracking()
            .Where(request => request.WorkAreaId == workAreaId &&
                              request.PurchaseRequestId != excludePurchaseRequestId &&
                              open.Contains(request.Status))
            .SelectMany(request => request.Items)
            .GroupBy(item => item.MaterialId)
            .Select(group => new QuantityByMaterial(workAreaId, group.Key, group.Sum(item => item.RequestedQuantity)))
            .ToListAsync(cancellationToken);
    }

    public void Add(PurchaseRequest request) => dbContext.PurchaseRequests.Add(request);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(PurchaseRequestItemConfiguration.RequestMaterialIndex))
        {
            throw DomainRuleException.Validation(
                "PURCHASE_REQUEST_DUPLICATE_MATERIAL",
                "Each material can appear only once in a purchase request.");
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is PurchaseRequest))
        {
            throw new DomainRuleException(
                "PURCHASE_REQUEST_VERSION_MISMATCH",
                "The purchase request was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
