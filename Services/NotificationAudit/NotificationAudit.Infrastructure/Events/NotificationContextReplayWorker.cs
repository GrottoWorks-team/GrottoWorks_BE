using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationAudit.Application.Events;

namespace NotificationAudit.Infrastructure.Events;

public sealed class NotificationContextReplayOptions
{
    public const string SectionName = "NotificationContextReplay";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 100;
}

public sealed class NotificationContextReplayWorker(IServiceScopeFactory scopes, IOptions<NotificationContextReplayOptions> options,
    TimeProvider clock, ILogger<NotificationContextReplayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NotificationReplayCursor? cursor = null;
        var cutoff = clock.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                cursor = await ReplayPageAsync(cutoff, cursor, stoppingToken);
                if (cursor is null) cutoff = clock.GetUtcNow();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // Preserve the cursor on infrastructure failure and retry the page on the next tick.
                logger.LogWarning("Notification context replay failed with {ErrorType}", exception.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task<NotificationReplayCursor?> ReplayPageAsync(DateTimeOffset cutoff, NotificationReplayCursor? cursor,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RetainedNotificationEvent> rows;
        await using (var scope = scopes.CreateAsyncScope())
        {
            rows = await scope.ServiceProvider.GetRequiredService<IUnresolvedNotificationEventStore>()
                .GetPageAsync(cutoff, cursor, options.Value.BatchSize, cancellationToken);
        }

        foreach (var row in rows)
        {
            try
            {
                // A failed event cannot leave tracked writes in the DbContext used by the next event.
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReplayNotificationContextHandler>().HandleAsync(row, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (InvalidNotificationEventException)
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IUnresolvedNotificationEventStore>().QuarantineAsync(row.EventId, cancellationToken);
                logger.LogWarning("Notification context {EventId} quarantined as invalid", row.EventId);
            }
            catch (Exception exception)
            {
                // The inbox remains pending. A later full pass retries it without blocking other events.
                logger.LogWarning("Notification context {EventId} remains pending after {ErrorType}", row.EventId, exception.GetType().Name);
            }
        }

        if (rows.Count < options.Value.BatchSize) return null;
        var last = rows[^1];
        return new NotificationReplayCursor(last.ReceivedAt, last.EventId);
    }
}
