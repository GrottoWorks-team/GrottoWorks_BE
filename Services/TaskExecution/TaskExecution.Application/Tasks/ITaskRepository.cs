using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Tasks;

public interface ITaskRepository
{
    Task<WorkTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<WorkTask> Items, long TotalItems)> GetPagedByWorkAreaAsync(
        Guid workAreaId,
        TaskState? status,
        TaskType? taskType,
        int page,
        int size,
        string? sort,
        CancellationToken cancellationToken = default);

    void Add(WorkTask task);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
