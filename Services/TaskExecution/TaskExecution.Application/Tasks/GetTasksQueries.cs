using BuildingBlocks.Web;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Tasks;

public sealed record GetTasksQuery(
    Guid WorkAreaId,
    TaskState? Status,
    TaskType? TaskType,
    int Page,
    int Size,
    string? Sort);

public sealed class GetTasksQueryHandler(ITaskRepository tasks, TaskScopeAccess scopeAccess)
{
    public async Task<(IReadOnlyList<TaskDto> Items, PageMeta Page)> HandleAsync(
        GetTasksQuery query,
        CancellationToken cancellationToken = default)
    {
        await scopeAccess.GetWorkAreaForReadAsync(query.WorkAreaId, cancellationToken);

        var (items, totalItems) = await tasks.GetPagedByWorkAreaAsync(
            query.WorkAreaId,
            query.Status,
            query.TaskType,
            query.Page,
            query.Size,
            query.Sort,
            cancellationToken);

        var totalPages = totalItems == 0 ? 0 : (long)Math.Ceiling(totalItems / (double)query.Size);
        return (
            items.Select(TaskDto.From).ToList(),
            new PageMeta(query.Page, query.Size, totalItems, totalPages));
    }
}

public sealed class GetTaskQueryHandler(ITaskRepository tasks, TaskScopeAccess scopeAccess)
{
    public async Task<TaskDto> HandleAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdAsync(taskId, cancellationToken);
        if (task is null)
        {
            throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        }

        await scopeAccess.GetWorkAreaForReadAsync(task.WorkAreaId, cancellationToken);
        return TaskDto.From(task);
    }
}
