using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Files;

internal sealed class ParishMapAttachmentWorker(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<ParishMapAttachmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RecoverBatchAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Parish map attachment scan failed ({ErrorType})", error.GetType().Name);
            }
        }
    }

    private async Task RecoverBatchAsync(CancellationToken ct)
    {
        using var batch = scopes.CreateScope();
        var db = batch.ServiceProvider.GetRequiredService<ParishCoordinationDbContext>();
        var now = clock.GetUtcNow();
        var pending = await db.Set<ParishMapAttachmentIntent>().AsNoTracking()
            .Where(row => row.NextAttemptAt <= now)
            .OrderBy(row => row.NextAttemptAt).ThenBy(row => row.Id).Take(25).ToListAsync(ct);
        foreach (var intent in pending)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var recovery = scope.ServiceProvider.GetRequiredService<ParishMapAttachmentRecovery>();
                await recovery.RecoverAsync(intent.ParishId, intent.FileId, intent.Id, ct);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Parish map attachment {ReservationId} will retry ({ErrorType})",
                    intent.Id, error.GetType().Name);
                var retryAt = clock.GetUtcNow().AddMinutes(1);
                await db.Set<ParishMapAttachmentIntent>().Where(row => row.Id == intent.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.NextAttemptAt, retryAt), ct);
            }
        }
    }
}
