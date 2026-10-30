using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Scopes;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Scopes;

public sealed class ScopeSnapshotTransaction(TaskExecutionDbContext dbContext) : IScopeSnapshotTransaction
{
    public async Task<T> ExecuteAsync<T>(string scopeType, Guid scopeId, Func<Task<T>> apply,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var key = $"task-scope:{scopeType}:{scopeId:D}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);

        var result = await apply();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
