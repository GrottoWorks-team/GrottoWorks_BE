using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Domain.SystemJobs;

namespace NotificationAudit.Infrastructure.SystemJobs;

public abstract class DurableSystemJobWorker(IServiceScopeFactory scopes, ILogger logger, SystemJobKind kind) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SystemJob? job;
                await using (var claimScope = scopes.CreateAsyncScope())
                    job = await claimScope.ServiceProvider.GetRequiredService<ISystemJobExecutionStore>().ClaimAsync(kind, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }
                using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var heartbeat = RenewLeaseAsync(job, jobCancellation, heartbeatCancellation.Token);
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ExecuteSystemJob>().HandleAsync(job, jobCancellation.Token);
                }
                finally
                {
                    await heartbeatCancellation.CancelAsync();
                    try { await heartbeat; } catch (OperationCanceledException) { }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("System {Kind} worker could not finish this iteration; durable leases will recover it.", kind);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task RenewLeaseAsync(SystemJob job, CancellationTokenSource jobCancellation, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await using var scope = scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<ISystemJobExecutionStore>()
                    .RenewAsync(job.Id, job.LeaseToken!.Value, ct))
                {
                    await jobCancellation.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { await jobCancellation.CancelAsync(); }
    }
}

public sealed class SystemExportWorker(IServiceScopeFactory scopes, ILogger<SystemExportWorker> logger)
    : DurableSystemJobWorker(scopes, logger, SystemJobKind.Export);
