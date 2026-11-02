using Microsoft.EntityFrameworkCore;
using Readiness.Application.Scope;
using Readiness.Domain;
using Readiness.Domain.Scope;
using Readiness.Infrastructure.Data;

namespace Readiness.Infrastructure.Scope;

public sealed class TaskProjectionStore(ReadinessDbContext dbContext) : ITaskProjectionStore
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
