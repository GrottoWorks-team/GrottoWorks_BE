using BuildingBlocks.Web;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Api.Common;

public sealed class DatabaseReadinessProbe(NotificationAuditDbContext dbContext) : IReadinessProbe
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.CanConnectAsync(cancellationToken);
}
