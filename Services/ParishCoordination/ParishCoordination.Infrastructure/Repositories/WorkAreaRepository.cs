using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Repositories;

public sealed class WorkAreaRepository(ParishCoordinationDbContext dbContext)
    : IWorkAreaRepository
{
    public void Add(WorkArea workArea)
    {
        dbContext.WorkAreas.Add(workArea);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new WorkAreaConcurrencyException(exception);
        }
    }

    public async Task<(IReadOnlyList<WorkArea> Items, long TotalItems)> GetPagedBySeasonIdAsync(
        Guid seasonId,
        int page,
        int size,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        var workAreas = dbContext.WorkAreas
            .AsNoTracking()
            .Where(workArea => workArea.SeasonId == seasonId);

        var sortParts = (sort ?? "workAreaName,asc").Split(',');
        var field = sortParts[0];
        var descending = sortParts.Length == 2 &&
            string.Equals(sortParts[1], "desc", StringComparison.OrdinalIgnoreCase);

        workAreas = field switch
        {
            "name" or "workAreaName" => descending
                ? workAreas.OrderByDescending(workArea => workArea.Name).ThenByDescending(workArea => workArea.Id)
                : workAreas.OrderBy(workArea => workArea.Name).ThenBy(workArea => workArea.Id),
            "status" => descending
                ? workAreas.OrderByDescending(workArea => workArea.Status).ThenByDescending(workArea => workArea.Id)
                : workAreas.OrderBy(workArea => workArea.Status).ThenBy(workArea => workArea.Id),
            "id" => descending
                ? workAreas.OrderByDescending(workArea => workArea.Id)
                : workAreas.OrderBy(workArea => workArea.Id),
            _ => workAreas.OrderBy(workArea => workArea.Name).ThenBy(workArea => workArea.Id)
        };

        var totalItems = await workAreas.LongCountAsync(cancellationToken);
        var items = await workAreas
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return (items, totalItems);
    }

    public async Task<WorkArea?> GetByIdAsync(
        Guid workAreaId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.WorkAreas
            .AsNoTracking()
            .SingleOrDefaultAsync(
                workArea => workArea.Id == workAreaId,
                cancellationToken);
    }

    public async Task<WorkArea?> GetByIdForUpdateAsync(
        Guid workAreaId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.WorkAreas
            .SingleOrDefaultAsync(
                workArea => workArea.Id == workAreaId,
                cancellationToken);
    }
}
