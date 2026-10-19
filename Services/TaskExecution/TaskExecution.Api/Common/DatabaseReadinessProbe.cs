using BuildingBlocks.Web;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Api.Common;

public sealed class DatabaseReadinessProbe(TaskExecutionDbContext dbContext) : IReadinessProbe
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.CanConnectAsync(cancellationToken);
}
