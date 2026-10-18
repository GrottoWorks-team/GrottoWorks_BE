using Microsoft.EntityFrameworkCore;
using Resource.Application.Scope;
using Resource.Domain.Scope;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Scope;

public sealed class WorkAreaScopeStore(ResourceDbContext dbContext) : IWorkAreaScopeStore
{
    public Task<WorkAreaScope?> GetAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
        dbContext.WorkAreaScopes.FirstOrDefaultAsync(scope => scope.WorkAreaId == workAreaId, cancellationToken);

    public async Task<IReadOnlyList<WorkAreaScope>> ListBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await dbContext.WorkAreaScopes.Where(scope => scope.SeasonId == seasonId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkAreaScope>> ListByIdsAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default)
    {
        if (workAreaIds.Count == 0)
        {
            return [];
        }

        return await dbContext.WorkAreaScopes.AsNoTracking()
            .Where(scope => workAreaIds.Contains(scope.WorkAreaId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkAreaScope>> ListByParishAsync(Guid parishId, Guid? seasonId, CancellationToken cancellationToken = default)
    {
        var query = dbContext.WorkAreaScopes.AsNoTracking().Where(scope => scope.ParishId == parishId);
        if (seasonId is { } season)
        {
            query = query.Where(scope => scope.SeasonId == season);
        }

        return await query.OrderBy(scope => scope.Name).ToListAsync(cancellationToken);
    }

    public void Add(WorkAreaScope scope) => dbContext.WorkAreaScopes.Add(scope);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
