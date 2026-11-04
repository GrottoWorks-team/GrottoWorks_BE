using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskExecution.Application.Tasks;
using TaskExecution.Infrastructure.Data;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Infrastructure.Tasks;

public sealed class PurchaseTaskRecoveryWorker(IServiceScopeFactory scopes,ILogger<PurchaseTaskRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Guid? after=null;
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30));
        while(await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<TaskExecutionDbContext>();
                var query=db.Tasks.AsNoTracking().Where(row=>row.Type==TaskType.Purchase &&
                    row.Status!=TaskExecution.Domain.Tasks.TaskStatus.Completed && row.Status!=TaskExecution.Domain.Tasks.TaskStatus.Cancelled);
                if(after.HasValue) query=query.Where(row=>row.Id.CompareTo(after.Value)>0);
                var rows=await query.OrderBy(row=>row.Id).Select(row=>new{row.Id,row.PurchaseRequestId}).Take(50).ToListAsync(ct);
                foreach(var row in rows)
                {
                    try
                    {
                        using var itemScope=scopes.CreateScope();
                        var snapshot=await itemScope.ServiceProvider.GetRequiredService<ITaskResourceGateway>().PurchaseRequestAsync(row.PurchaseRequestId!.Value,ct);
                        await itemScope.ServiceProvider.GetRequiredService<PurchaseTaskEventHandler>().ReconcileAsync(row.Id,snapshot,ct);
                    }
                    catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                    catch(Exception exception){logger.LogWarning(exception,"Purchase task {TaskId} reconciliation deferred",row.Id);}
                }
                after=rows.Count==50 ? rows[^1].Id : null;
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception exception){logger.LogError(exception,"Purchase task recovery scan failed");}
        }
    }
}
