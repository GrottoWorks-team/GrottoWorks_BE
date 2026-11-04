using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using TaskExecution.Infrastructure.Data;
namespace TaskExecution.Api.Diagnostics;
public sealed class ServiceQueueProbe(TaskExecutionDbContext db) : IIntegrationQueueProbe
{
    public string Service => "TaskExecution";
    public Task<IReadOnlyList<IntegrationQueueState>> ReadAsync(CancellationToken ct) =>
        IntegrationQueueReader.ReadAsync(db.Database.GetDbConnection(),new Dictionary<string,string>
        {
            ["outbox"] = "SELECT count(*),COALESCE(sum(attempts),0)::bigint FROM task_event_outbox WHERE published_at IS NULL",
            ["file-recovery"] = "SELECT count(*),COALESCE(sum(attempts),0)::bigint FROM task_file_attachment_intent WHERE completed_at IS NULL",
        },ct);
}
