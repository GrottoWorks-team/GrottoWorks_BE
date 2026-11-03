using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Resource.Application.Abstractions;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Inventory;

/// <summary>
/// Transaction-scoped PostgreSQL advisory lock per work area (<c>pg_advisory_xact_lock</c>): concurrent stock
/// outflows of the same work area queue up; different work areas never block each other. The lock is released
/// when the transaction commits or rolls back.
/// </summary>
public sealed class PostgresWorkAreaStockLock(ResourceDbContext dbContext) : IWorkAreaStockLock
{
    public async Task<IStockLease> AcquireAsync(Guid workAreaId, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            var existingKey = BitConverter.ToInt64(workAreaId.ToByteArray(), 0);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({existingKey})", cancellationToken);
            return new BorrowedLease();
        }
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var key = BitConverter.ToInt64(workAreaId.ToByteArray(), 0);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            return new Lease(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    // The approval operation owns this transaction; releasing a stock lease must not commit it early.
    private sealed class BorrowedLease : IStockLease
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Lease(IDbContextTransaction transaction) : IStockLease
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
