using Microsoft.EntityFrameworkCore;
using Reporting.Application.Scope;
using Reporting.Domain.Scope;
using Reporting.Infrastructure.Data;

namespace Reporting.Infrastructure.Scope;

public sealed class TaskProjectionStore(ReportingDbContext dbContext) : ITaskProjectionStore
{
    public Task<TaskProjection?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        dbContext.TaskProjections.FirstOrDefaultAsync(task => task.TaskId == taskId, cancellationToken);

    public void Add(TaskProjection task) => dbContext.TaskProjections.Add(task);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
