using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskExecution.Application.Support;

namespace TaskExecution.Infrastructure.Support;

/// <summary>Every pass scans all pending attempts; an unavailable source does not starve later items.</summary>
public sealed class SupportMaterialApprovalWorker(IServiceScopeFactory scopes, ILogger<SupportMaterialApprovalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Guid? cursor=null;
                while (!stoppingToken.IsCancellationRequested)
                {
                    IReadOnlyList<Guid> ids;
                    await using (var scope=scopes.CreateAsyncScope())
                        ids=await scope.ServiceProvider.GetRequiredService<ISupportMaterialSyncStore>().GetPendingItemsAsync(cursor,100,stoppingToken);
                    if (ids.Count==0) break;
                    foreach (var id in ids)
                    {
                        try
                        {
                            await using var scope=scopes.CreateAsyncScope();
                            await scope.ServiceProvider.GetRequiredService<SupportMaterialApprovalProcessor>().ProcessAsync(id,stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        catch (Exception exception)
                        { logger.LogWarning("Support approval {ItemId} remains pending ({ErrorType}); it will be reconciled.",id,exception.GetType().Name); }
                    }
                    cursor=ids[^1];
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Support approval scan failed ({ErrorType}); will retry.",exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);
        }
    }
}
