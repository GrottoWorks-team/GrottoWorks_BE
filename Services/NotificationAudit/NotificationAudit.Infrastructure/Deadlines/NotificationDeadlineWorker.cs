using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationAudit.Application.Events;
using NotificationAudit.Domain.Notifications;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.Deadlines;

public sealed class NotificationDeadlineProcessor(NotificationAuditDbContext db,TimeProvider clock)
{
    public async Task ProcessAsync(Guid taskId,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var key=$"task-deadline:{taskId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))",ct);
        var task=await db.Set<NotificationTaskDeadline>().SingleOrDefaultAsync(row=>row.TaskId==taskId,ct);
        var now=clock.GetUtcNow();var kind=task is null ? null : TaskDeadlinePolicy.Due(task.Deadline,task.Status,now);
        if(task is null || kind is null){await tx.CommitAsync(ct);return;}
        var eventType=kind=="OVERDUE" ? "task.deadline.overdue" : "task.deadline.reminder";
        var enabled=await db.NotificationRules.AnyAsync(row=>row.EventType==eventType && row.InAppEnabled,ct);
        if(!enabled){await tx.CommitAsync(ct);return;}
        var disabled=await db.NotificationPreferences.Where(row=>task.Assignees.Contains(row.UserId) && !row.InAppEnabled).Select(row=>row.UserId).ToListAsync(ct);
        var delivered=await db.Set<NotificationDeadlineDelivery>().Where(row=>row.TaskId==taskId && row.DeadlineGeneration==task.DeadlineGeneration && row.Kind==kind)
            .Select(row=>row.RecipientId).ToListAsync(ct);
        foreach(var recipient in task.Assignees.Except(disabled).Except(delivered))
        {
            var notification=Notification.Create(recipient,eventType,kind=="OVERDUE" ? "Task overdue" : "Task deadline approaching",
                kind=="OVERDUE" ? "An assigned task has passed its deadline. Open the task to review its status."
                    : "An assigned task is due within 24 hours. Open the task to view details.","TASK",taskId,now);
            db.Notifications.Add(notification);
            db.Add(new NotificationDeadlineDelivery {TaskId=taskId,DeadlineGeneration=task.DeadlineGeneration,RecipientId=recipient,
                Kind=kind,NotificationId=notification.Id,CreatedAt=now});
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
public sealed class NotificationDeadlineWorker(IServiceScopeFactory scopes,ILogger<NotificationDeadlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Guid? cursor=null;var now=DateTimeOffset.UtcNow;
                while(!stoppingToken.IsCancellationRequested)
                {
                    Guid[] ids;
                    await using(var scope=scopes.CreateAsyncScope())
                    {
                        var db=scope.ServiceProvider.GetRequiredService<NotificationAuditDbContext>();
                        ids=await db.Set<NotificationTaskDeadline>().AsNoTracking().Where(row=>row.Deadline!=null && row.Deadline<=now.AddHours(24) &&
                            row.Status!="COMPLETED" && row.Status!="CANCELLED" && (!cursor.HasValue || row.TaskId.CompareTo(cursor.Value)>0))
                            .OrderBy(row=>row.TaskId).Take(100).Select(row=>row.TaskId).ToArrayAsync(stoppingToken);
                    }
                    if(ids.Length==0)break;
                    foreach(var id in ids)
                    {
                        try {await using var scope=scopes.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<NotificationDeadlineProcessor>().ProcessAsync(id,stoppingToken);}
                        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){throw;}
                        catch(Exception exception){logger.LogWarning("Deadline reminder {TaskId} failed ({ErrorType}); will retry.",id,exception.GetType().Name);}
                    }
                    cursor=ids[^1];
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception exception){logger.LogWarning("Deadline scan failed ({ErrorType}); will retry.",exception.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(30),stoppingToken);
        }
    }
}
