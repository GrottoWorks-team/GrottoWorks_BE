using MassTransit;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotificationAudit.Infrastructure.Data;
using TaskExecution.Contracts.Events;

namespace NotificationAudit.Infrastructure.Deadlines;

public sealed class NotificationSnapshotIngestion(NotificationAuditDbContext db,TimeProvider clock)
{
    public async Task ApplyTaskAsync(TaskScopeSnapshot snapshot,CancellationToken ct)
    {
        if(snapshot.SchemaVersion!=1 || snapshot.EventId==Guid.Empty || snapshot.TaskId==Guid.Empty || snapshot.SourceVersion<=0 || snapshot.AssigneeUserIds is null ||
            snapshot.AssigneeUserIds.Any(id=>id==Guid.Empty) || snapshot.Season is null || snapshot.Season.ParishId==Guid.Empty || snapshot.WorkArea is null ||
            snapshot.WorkAreaId!=snapshot.WorkArea.WorkAreaId || snapshot.Season.SeasonId!=snapshot.WorkArea.SeasonId ||
            snapshot.Season.ParishId!=snapshot.WorkArea.ParishId)
            throw new FormatException("Invalid task reminder snapshot.");
        await ExecuteAsync(snapshot.EventId,$"task-deadline:{snapshot.TaskId}",Fingerprint(snapshot),async ()=>
        {
            var row=await db.Set<NotificationTaskDeadline>().SingleOrDefaultAsync(item=>item.TaskId==snapshot.TaskId,ct);
            if(row is not null && row.SourceVersion>snapshot.SourceVersion)return;
            if(row is null){row=new NotificationTaskDeadline {TaskId=snapshot.TaskId};db.Add(row);}
            else if(row.SourceVersion==snapshot.SourceVersion && (row.Deadline.HasValue || snapshot.Deadline is null))return;
            // Deadline is required in the source domain. Null in legacy wire schemas means unknown, not clear.
            var deadline=snapshot.Deadline ?? row.Deadline;
            if(row.SourceVersion>0 && row.Deadline!=deadline)row.DeadlineGeneration++;
            row.ParishId=snapshot.Season.ParishId;row.SeasonId=snapshot.Season.SeasonId;row.SourceVersion=snapshot.SourceVersion;
            row.Deadline=deadline;row.Status=snapshot.Status;row.Assignees=snapshot.AssigneeUserIds.Distinct().ToArray();
        },ct);
    }
    private static string Fingerprint<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private async Task ExecuteAsync(Guid eventId,string key,string fingerprint,Func<Task> apply,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))",ct);
        var existing=await db.Set<NotificationSnapshotReceipt>().SingleOrDefaultAsync(row=>row.EventId==eventId,ct);
        if(existing is not null)
        {
            if(existing.SourceKey!=key || existing.Fingerprint!=fingerprint)throw new FormatException("Snapshot identity was reused with different source context.");
            await tx.CommitAsync(ct);return;
        }
        await apply();db.Add(new NotificationSnapshotReceipt {EventId=eventId,SourceKey=key,Fingerprint=fingerprint,ReceivedAt=clock.GetUtcNow()});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
public sealed class NotificationTaskSnapshotConsumer(NotificationSnapshotIngestion ingestion) : IConsumer<TaskScopeSnapshot>
{public Task Consume(ConsumeContext<TaskScopeSnapshot> context)=>ingestion.ApplyTaskAsync(context.Message,context.CancellationToken);}
public sealed class NotificationTaskSnapshotConsumerDefinition : ConsumerDefinition<NotificationTaskSnapshotConsumer>
{
    public NotificationTaskSnapshotConsumerDefinition(){EndpointName="notification-task-snapshots";ConcurrentMessageLimit=4;}
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint,IConsumerConfigurator<NotificationTaskSnapshotConsumer> consumer,IRegistrationContext context)=>Retry(endpoint);
    internal static void Retry(IReceiveEndpointConfigurator endpoint)=>endpoint.UseMessageRetry(retry=>{retry.Ignore<FormatException>();retry.Intervals(TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(15));});
}
