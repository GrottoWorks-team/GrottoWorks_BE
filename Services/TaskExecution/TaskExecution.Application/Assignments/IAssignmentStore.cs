using TaskExecution.Domain.Assignments;

namespace TaskExecution.Application.Assignments;

public interface IAssignmentStore
{
    Task<T> InTaskTransactionAsync<T>(Guid taskId, Func<Task<T>> action, CancellationToken cancellationToken);
    Task<IReadOnlyList<TaskAssignment>> ListAsync(Guid taskId, CancellationToken cancellationToken);
    Task<TaskAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken);
    Task<(IReadOnlyList<TaskAssignment> Items, long Total)> ApplicationsPageAsync(Guid taskId, int page, int size, bool descending, CancellationToken cancellationToken);
    Task<bool> HasScheduleConflictAsync(Guid volunteerId, Guid taskId, DateTimeOffset? startAt,
        DateTimeOffset deadline, CancellationToken cancellationToken);
    void Add(TaskAssignment assignment);
    Task SaveAsync(CancellationToken cancellationToken);
}
