namespace ParishCoordination.Infrastructure.TaskScopeSync;

/// <summary>Replace this adapter when scope delivery moves to the message broker.</summary>
public interface ITaskScopeDelivery
{
    Task DeliverAsync(TaskScopeOutbox message, CancellationToken cancellationToken);
}
