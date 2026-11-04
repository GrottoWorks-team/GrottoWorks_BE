using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Tasks;

public sealed class FileAttachmentRecovery(TaskExecutionDbContext db,IAssignmentStore transactions,ITaskFileGateway files)
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        var now=DateTimeOffset.UtcNow;
        var pending=await db.Set<FileAttachmentIntent>().AsNoTracking().Where(row=>row.CompletedAt==null && row.NextAttemptAt<=now)
            .OrderBy(row=>row.NextAttemptAt).Select(row=>new {row.Id,row.TaskId}).Take(50).ToListAsync(ct);
        foreach(var item in pending)
        {
            await transactions.InTaskTransactionAsync(item.TaskId,async ()=>
            {
                var row=await db.Set<FileAttachmentIntent>().SingleAsync(row=>row.Id==item.Id,ct);
                if(row.CompletedAt.HasValue || row.NextAttemptAt>DateTimeOffset.UtcNow) return false;
                try
                {
                    var committed=await db.Set<TaskProgress>().AnyAsync(p=>p.Id==row.ProgressId,ct);
                    if(committed) await files.FinalizeAsync(row.ToIntent(),ct);
                    else await files.ReleaseAsync(row.ToIntent(),ct);
                    row.Outcome=committed ? "FINALIZED" : "RELEASED";row.CompletedAt=DateTimeOffset.UtcNow;
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
                catch(TaskExecution.Domain.DomainRuleException)
                {
                    row.Attempts++;row.NextAttemptAt=DateTimeOffset.UtcNow.AddSeconds(Math.Min(300,5*row.Attempts));
                }
                await db.SaveChangesAsync(ct);return true;
            },ct);
        }
    }
}
public sealed class FileAttachmentRecoveryWorker(IServiceScopeFactory scopes,ILogger<FileAttachmentRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<FileAttachmentRecovery>().ProcessAsync(stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch(Exception exception) { logger.LogError(exception,"Task evidence attachment recovery failed"); }
        }
    }
}
