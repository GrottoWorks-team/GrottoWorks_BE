using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Infrastructure.Tasks;

public sealed class TaskRepository(TaskExecutionDbContext dbContext) : ITaskRepository
{
    public Task<WorkTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        dbContext.Tasks.AsNoTracking().SingleOrDefaultAsync(task => task.Id == taskId, cancellationToken);

    public async Task<(IReadOnlyList<WorkTask> Items, long TotalItems)> GetPagedByWorkAreaAsync(
        Guid workAreaId,
        TaskState? status,
        TaskType? taskType,
        int page,
        int size,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Tasks.AsNoTracking().Where(task => task.WorkAreaId == workAreaId);
        if (status.HasValue)
        {
            query = query.Where(task => task.Status == status.Value);
        }

        if (taskType.HasValue)
        {
            query = query.Where(task => task.Type == taskType.Value);
        }

        query = ApplySorting(query, sort);
        var totalItems = await query.LongCountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return (items, totalItems);
    }

    public void Add(WorkTask task) => dbContext.Tasks.Add(task);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private static IQueryable<WorkTask> ApplySorting(IQueryable<WorkTask> query, string? sort)
    {
        var parts = (sort ?? "createdAt,desc").Split(',');
        var field = parts[0];
        var descending = parts.Length == 2 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

        return field switch
        {
            "name" => descending
                ? query.OrderByDescending(task => task.Name).ThenByDescending(task => task.Id)
                : query.OrderBy(task => task.Name).ThenBy(task => task.Id),
            "deadline" => descending
                ? query.OrderByDescending(task => task.Deadline).ThenByDescending(task => task.Id)
                : query.OrderBy(task => task.Deadline).ThenBy(task => task.Id),
            "status" => descending
                ? query.OrderByDescending(task => task.Status).ThenByDescending(task => task.Id)
                : query.OrderBy(task => task.Status).ThenBy(task => task.Id),
            "priority" => descending
                ? query.OrderByDescending(task => task.Priority).ThenByDescending(task => task.Id)
                : query.OrderBy(task => task.Priority).ThenBy(task => task.Id),
            _ => descending
                ? query.OrderByDescending(task => task.CreatedAt).ThenByDescending(task => task.Id)
                : query.OrderBy(task => task.CreatedAt).ThenBy(task => task.Id)
        };
    }
}
