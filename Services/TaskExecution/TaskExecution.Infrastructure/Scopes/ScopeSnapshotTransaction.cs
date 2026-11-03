using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Scopes;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Scopes;

public sealed class ScopeSnapshotTransaction(TaskExecutionDbContext dbContext) : IScopeSnapshotTransaction
{
    public async Task<T> ExecuteAsync<T>(string scopeType, Guid scopeId, Func<Task<T>> apply,
        CancellationToken cancellationToken = default)
    {
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        var key = $"task-scope:{scopeType}:{scopeId:D}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);

        var result = await apply();
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
