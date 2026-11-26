using Microsoft.EntityFrameworkCore;
using Resource.Application;
using Resource.Application.Inventory;
using Resource.Domain.Inventory;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Inventory;

public sealed class ParishDistributionStore(ResourceDbContext dbContext) : IParishDistributionStore
{
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await operation();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<ParishDistribution?> GetAsync(Guid distributionId, CancellationToken cancellationToken = default) =>
        dbContext.ParishDistributions
            .Include(distribution => distribution.Items)
            .FirstOrDefaultAsync(distribution => distribution.ParishDistributionId == distributionId, cancellationToken);

    public async Task<PagedResult<ParishDistribution>> SearchAsync(
        ParishDistributionSearch search,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ParishDistributions.AsNoTracking()
            .Include(distribution => distribution.Items)
            .Where(distribution => distribution.ParishId == search.ParishId);
        if (search.SeasonId is { } seasonId)
        {
            query = query.Where(distribution => distribution.SeasonId == seasonId);
        }

        if (search.WorkAreaIds is { } workAreaIds)
        {
            query = query.Where(distribution => workAreaIds.Contains(distribution.TargetWorkAreaId));
        }

        if (search.Status is { } status)
        {
            query = query.Where(distribution => distribution.Status == status);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(distribution => distribution.IssuedAt)
            .ThenBy(distribution => distribution.ParishDistributionId)
            .Skip((search.Page - 1) * search.Size)
            .Take(search.Size)
            .ToListAsync(cancellationToken);
        return new PagedResult<ParishDistribution>(items, total);
    }

    public void Add(ParishDistribution distribution) => dbContext.ParishDistributions.Add(distribution);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
