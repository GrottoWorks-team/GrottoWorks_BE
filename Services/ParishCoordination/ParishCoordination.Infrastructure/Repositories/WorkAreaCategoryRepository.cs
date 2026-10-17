using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.WorkAreaCategories;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Repositories;

public sealed class WorkAreaCategoryRepository(
    ParishCoordinationDbContext dbContext)
    : IWorkAreaCategoryRepository
{
    public async Task<IReadOnlyList<WorkAreaCategory>> GetAllAsync(
        WorkAreaCategoryStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.WorkAreaCategories.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(category => category.Status == status.Value);
        }

        return await query
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .ToListAsync(cancellationToken);
    }
}
