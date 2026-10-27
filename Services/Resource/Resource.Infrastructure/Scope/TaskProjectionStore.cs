using Microsoft.EntityFrameworkCore;
using Resource.Application.Scope;
using Resource.Domain.Scope;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Scope;

public sealed class TaskProjectionStore(ResourceDbContext dbContext) : ITaskProjectionStore
{
    public Task<TaskProjection?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        dbContext.TaskProjections.FirstOrDefaultAsync(task => task.TaskId == taskId, cancellationToken);

    public async Task<IReadOnlyList<TaskProjection>> ListByPurchaseRequestAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default) =>
        await dbContext.TaskProjections.AsNoTracking().Where(task => task.PurchaseRequestId == purchaseRequestId).ToListAsync(cancellationToken);

    public void Add(TaskProjection task) => dbContext.TaskProjections.Add(task);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
