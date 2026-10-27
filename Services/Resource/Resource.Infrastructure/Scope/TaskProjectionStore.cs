using Microsoft.EntityFrameworkCore;
using Resource.Application.Scope;
using Resource.Domain.Scope;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Scope;

public sealed class TaskProjectionStore(ResourceDbContext dbContext) : ITaskProjectionStore
{
    public Task<TaskProjection?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        dbContext.TaskProjections.FirstOrDefaultAsync(task => task.TaskId == taskId, cancellationToken);

    public void Add(TaskProjection task) => dbContext.TaskProjections.Add(task);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
