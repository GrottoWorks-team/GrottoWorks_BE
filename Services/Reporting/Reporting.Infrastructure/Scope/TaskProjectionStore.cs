using Microsoft.EntityFrameworkCore;
using Reporting.Application.Scope;
using Reporting.Domain;
using Reporting.Domain.Scope;
using Reporting.Infrastructure.Data;

namespace Reporting.Infrastructure.Scope;

public sealed class TaskProjectionStore(ReportingDbContext dbContext) : ITaskProjectionStore
{
    public Task<TaskProjection?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        dbContext.TaskProjections.FirstOrDefaultAsync(task => task.TaskId == taskId, cancellationToken);

    public void Add(TaskProjection task) => dbContext.TaskProjections.Add(task);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.IsAnyUniqueViolation())
        {
            // Another snapshot or task event changed the same task first: the caller (or the broker) delivers it again.
            throw new DomainRuleException("TASK_PROJECTION_CONFLICT", "The task projection was updated concurrently; deliver the change again.");
        }
    }
}
