namespace TaskExecution.Application.Scopes;

/// <summary>Serializes snapshot ingestion for one scope and commits its changes atomically.</summary>
public interface IScopeSnapshotTransaction
{
    Task<T> ExecuteAsync<T>(string scopeType, Guid scopeId, Func<Task<T>> apply,
        CancellationToken cancellationToken = default);
}
