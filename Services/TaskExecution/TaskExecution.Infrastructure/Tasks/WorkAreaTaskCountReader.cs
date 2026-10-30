using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;
using DomainTaskStatus = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Infrastructure.Tasks;

public sealed class WorkAreaTaskCountReader(TaskExecutionDbContext dbContext)
    : IWorkAreaTaskCountReader
{
    public async Task<IReadOnlyList<WorkAreaTaskCountValues>> GetAsync(
        IReadOnlyCollection<Guid> workAreaIds,
        DateTimeOffset calculatedAt,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Tasks
            .AsNoTracking()
            .Where(task => workAreaIds.Contains(task.WorkAreaId))
            .GroupBy(task => task.WorkAreaId)
            .Select(group => new WorkAreaTaskCountValues(
                group.Key,
                group.LongCount(),
                group.LongCount(task => task.Status == DomainTaskStatus.Cancelled),
                group.LongCount(task => task.Status == DomainTaskStatus.Completed),
                group.LongCount(task =>
                    task.Status != DomainTaskStatus.Completed &&
                    task.Status != DomainTaskStatus.Cancelled &&
                    task.Deadline < calculatedAt)))
            .ToListAsync(cancellationToken);
    }
}
