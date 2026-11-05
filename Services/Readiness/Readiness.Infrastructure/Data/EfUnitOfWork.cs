using Readiness.Application.Abstractions;

namespace Readiness.Infrastructure.Data;

public sealed class EfUnitOfWork(ReadinessDbContext dbContext) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await operation();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
