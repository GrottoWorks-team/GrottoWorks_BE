namespace Resource.Application.Abstractions;

/// <summary>
/// Serialises commands that take stock out of a work area (allocation, transfer out, adjustments — BR-33, BA edge
/// case 19 "two officers allocate the last units"). Held inside one database transaction: read balances, check,
/// write, then <see cref="IStockLease.CommitAsync"/>. Disposing without commit rolls back.
/// </summary>
public interface IWorkAreaStockLock
{
    Task<IStockLease> AcquireAsync(Guid workAreaId, CancellationToken cancellationToken = default);
}

public interface IStockLease : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
