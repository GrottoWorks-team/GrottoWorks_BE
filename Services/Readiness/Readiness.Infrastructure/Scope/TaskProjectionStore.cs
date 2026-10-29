using Microsoft.EntityFrameworkCore;
using Readiness.Application.Scope;
using Readiness.Domain.Scope;
using Readiness.Infrastructure.Data;

namespace Readiness.Infrastructure.Scope;

public sealed class TaskProjectionStore(ReadinessDbContext dbContext) : ITaskProjectionStore
{
    public Task<TaskProjection?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        dbContext.TaskProjections.FirstOrDefaultAsync(task => task.TaskId == taskId, cancellationToken);

    public void Add(TaskProjection task) => dbContext.TaskProjections.Add(task);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
