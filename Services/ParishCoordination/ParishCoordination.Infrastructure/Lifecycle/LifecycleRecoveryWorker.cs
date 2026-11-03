using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Lifecycle;

public sealed class LifecycleRecoveryWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<LifecycleRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("LifecycleEvents:RecoveryIntervalSeconds", 30), 5, 3600));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Guid? cursor = null;
                while (!stoppingToken.IsCancellationRequested)
                {
                    Guid[] ids;
                    using (var scope = scopes.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ParishCoordinationDbContext>();
                        var mutableAreas = from area in db.WorkAreas
                            join season in db.Seasons on area.SeasonId equals season.Id
                            where season.Status == SeasonStatus.Active || season.Status == SeasonStatus.Ready
                            select area.Id;
                        var pendingAreas = db.Set<LifecycleInboxMessage>().Where(x => x.ProcessedAt == null).Select(x => x.WorkAreaId);
                        ids = await mutableAreas.Union(pendingAreas).Where(id => !cursor.HasValue || id.CompareTo(cursor.Value) > 0)
                            .OrderBy(id => id).Take(100).ToArrayAsync(stoppingToken);
                    }
                    if (ids.Length == 0) break;
                    foreach (var id in ids)
                    {
                        try
                        {
                            using var scope = scopes.CreateScope();
                            await scope.ServiceProvider.GetRequiredService<LifecycleEventProcessor>().RecoverAreaAsync(id, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        catch (Exception exception)
                        {
                            logger.LogWarning("Lifecycle recovery for Work Area {WorkAreaId} failed ({ErrorType}); retained for retry.", id, exception.GetType().Name);
                        }
                    }
                    cursor = ids[^1];
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Lifecycle recovery scan failed ({ErrorType}).", exception.GetType().Name); }
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
