using Microsoft.EntityFrameworkCore;
using Resource.Application;
using Resource.Application.Inventory;
using Resource.Domain.Inventory;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Inventory;

public sealed class AllocationStore(ResourceDbContext dbContext) : IAllocationStore
{
    public async Task<PagedResult<MaterialAllocation>> SearchAsync(AllocationSearch search, CancellationToken cancellationToken = default)
    {
        var query = dbContext.MaterialAllocations.AsNoTracking().Where(allocation => search.WorkAreaIds.Contains(allocation.WorkAreaId));
        if (search.TaskId is { } taskId)
        {
            query = query.Where(allocation => allocation.TaskId == taskId);
        }

        if (search.MaterialId is { } materialId)
        {
            query = query.Where(allocation => allocation.Items.Any(item => item.MaterialId == materialId));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(allocation => allocation.AllocationDate)
            .ThenByDescending(allocation => allocation.MaterialAllocationId)
            .Skip((search.Page - 1) * search.Size)
            .Take(search.Size)
            .Include(allocation => allocation.Items)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return new PagedResult<MaterialAllocation>(items, total);
    }

    public async Task<IReadOnlyList<MaterialAllocationItem>> ListItemsOfTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        await dbContext.MaterialAllocations.AsNoTracking()
            .Where(allocation => allocation.TaskId == taskId)
            .SelectMany(allocation => allocation.Items)
            .ToListAsync(cancellationToken);

    public void Add(MaterialAllocation allocation) => dbContext.MaterialAllocations.Add(allocation);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
