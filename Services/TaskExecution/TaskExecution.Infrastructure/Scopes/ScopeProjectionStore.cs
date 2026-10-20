using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain.Scopes;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Scopes;

public sealed class ScopeProjectionStore(TaskExecutionDbContext dbContext) : IScopeProjectionStore
{
    public Task<SeasonScope?> GetSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        dbContext.SeasonScopes.SingleOrDefaultAsync(scope => scope.SeasonId == seasonId, cancellationToken);

    public Task<WorkAreaScope?> GetWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
        dbContext.WorkAreaScopes.SingleOrDefaultAsync(scope => scope.WorkAreaId == workAreaId, cancellationToken);

    public void Add(SeasonScope scope) => dbContext.SeasonScopes.Add(scope);

    public void Add(WorkAreaScope scope) => dbContext.WorkAreaScopes.Add(scope);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
