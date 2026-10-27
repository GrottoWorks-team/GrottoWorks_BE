using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Reporting.Application.Exports;

namespace Reporting.Infrastructure.Exports;

/// <summary>
/// F-RPT-06 background worker: runs queued export jobs one by one (woken by <see cref="ExportSignal"/>, polling every
/// <see cref="PollInterval"/>) and re-queues jobs left RUNNING longer than <see cref="StaleAfter"/> by a crashed worker.
/// </summary>
public sealed class ReportExportWorker(IServiceScopeFactory scopes, ExportSignal signal, ILogger<ReportExportWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRecovery = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now - lastRecovery > StaleAfter)
                {
                    lastRecovery = now;
                    await using var recoveryScope = scopes.CreateAsyncScope();
                    var recovered = await recoveryScope.ServiceProvider.GetRequiredService<IReportExportStore>()
                        .RecoverStaleAsync(now - StaleAfter, now, stoppingToken);
                    if (recovered > 0)
                    {
                        logger.LogWarning("Re-queued {Count} stale report export job(s).", recovered);
                    }
                }

                // A fresh scope per job: a clean change tracker and no state shared between exports.
                while (!stoppingToken.IsCancellationRequested)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (!await scope.ServiceProvider.GetRequiredService<ReportExportRunner>().RunNextAsync(stoppingToken))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Database unavailable and the like: keep the worker alive and retry on the next tick.
                logger.LogError(exception, "Report export worker iteration failed.");
            }

            try
            {
                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
