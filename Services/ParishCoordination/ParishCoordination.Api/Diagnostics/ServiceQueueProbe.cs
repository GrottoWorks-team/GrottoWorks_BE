using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Infrastructure.Data;
namespace ParishCoordination.Api.Diagnostics;
public sealed class ServiceQueueProbe(ParishCoordinationDbContext db) : IIntegrationQueueProbe
{
    public string Service => "ParishCoordination";
    public Task<IReadOnlyList<IntegrationQueueState>> ReadAsync(CancellationToken ct) =>
        IntegrationQueueReader.ReadAsync(db.Database.GetDbConnection(),new Dictionary<string,string>
        {
            ["outbox"] = "SELECT count(*),COALESCE(sum(attempts),0)::bigint FROM planning_event_outbox WHERE published_at IS NULL",
            ["lifecycle-inbox"] = "SELECT count(*),0::bigint FROM lifecycle_inbox WHERE processed_at IS NULL",
        },ct);
}
