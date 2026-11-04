using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using NotificationAudit.Infrastructure.Data;
namespace NotificationAudit.Api.Diagnostics;
public sealed class ServiceQueueProbe(NotificationAuditDbContext db) : IIntegrationQueueProbe
{
    public string Service => "NotificationAudit";
    public Task<IReadOnlyList<IntegrationQueueState>> ReadAsync(CancellationToken ct) =>
        IntegrationQueueReader.ReadAsync(db.Database.GetDbConnection(),new Dictionary<string,string>
        {
            ["inbox"] = "SELECT count(*),0::bigint FROM notification_event_inbox WHERE processed_at IS NULL",
            ["jobs"] = "SELECT count(*),COALESCE(sum(attempts),0)::bigint FROM system_job WHERE finished_at IS NULL",
        },ct);
}
