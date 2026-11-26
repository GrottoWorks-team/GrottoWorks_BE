using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Resource.Application.Abstractions;
using Resource.Application.Inventory;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Inventory;

/// <summary>
/// Transaction-scoped advisory lock per (parish, season): concurrent parish warehouse outflows of the same season
/// queue up, other seasons never block. The lock releases on commit/rollback.
/// </summary>
public sealed class PostgresParishStockLock(ResourceDbContext dbContext) : IParishStockLock
{
    public async Task<IStockLease> AcquireAsync(Guid parishId, Guid seasonId, CancellationToken cancellationToken = default)
    {
        var key = LockKey(parishId, seasonId);
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            return new BorrowedLease();
        }

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            return new Lease(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private static long LockKey(Guid parishId, Guid seasonId)
    {
        var parish = parishId.ToByteArray();
        var season = seasonId.ToByteArray();
        var mixed = new byte[8];
        for (var index = 0; index < mixed.Length; index++)
        {
            mixed[index] = (byte)(parish[index] ^ season[index] ^ parish[index + 8] ^ season[index + 8]);
        }

        return BitConverter.ToInt64(mixed, 0);
    }

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
