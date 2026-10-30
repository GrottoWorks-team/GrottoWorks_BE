namespace ParishCoordination.Application.WorkAreas;

public sealed record WorkAreaTaskCounts(
    Guid WorkAreaId,
    long TotalTasks,
    long CancelledTasks,
    long EligibleTasks,
    long CompletedTasks,
    long OverdueTasks,
    DateTimeOffset CalculatedAt);

public interface IWorkAreaTaskCountClient
{
    Task<WorkAreaTaskCounts> GetAsync(
        Guid workAreaId,
        CancellationToken cancellationToken = default);
}

public sealed class WorkAreaTaskCountUnavailableException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);
