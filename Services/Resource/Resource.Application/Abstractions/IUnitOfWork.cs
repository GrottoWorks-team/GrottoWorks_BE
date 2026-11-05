namespace Resource.Application.Abstractions;

/// <summary>
/// One database transaction around a command whose state change and outbox events must commit together.
/// <see cref="IIntegrationEventPublisher"/> saves its outbox row separately, so a command that saves its own
/// changes first and publishes afterwards would otherwise commit the change even when the event is lost.
/// Joins the caller's transaction when one is already open (stock lease, store transaction).
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);

    async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        }, cancellationToken);
}
