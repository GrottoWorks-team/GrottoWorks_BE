using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Identity.Infrastructure.Data;

namespace Identity.Infrastructure.Events;

public sealed class IdentityEventDispatcher(IServiceScopeFactory scopes, ILogger<IdentityEventDispatcher> logger) : BackgroundService
{
    private const long DispatcherLock = 71110303;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
                if (DateTimeOffset.UtcNow - lastCleanup > TimeSpan.FromHours(1))
                {
                    using var cleanupScope = scopes.CreateScope();
                    var db = cleanupScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                    var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
                    await db.EventOutbox.Where(row => row.PublishedAt != null && row.PublishedAt < cutoff)
                        .ExecuteDeleteAsync(stoppingToken);
                    lastCleanup = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Identity event dispatcher failed ({ErrorType}).", exception.GetType().Name);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var lockCommand = db.Database.GetDbConnection().CreateCommand();
        lockCommand.Transaction = transaction.GetDbTransaction();
        lockCommand.CommandText = $"SELECT pg_try_advisory_xact_lock({DispatcherLock})";
        if (!(bool)(await lockCommand.ExecuteScalarAsync(cancellationToken))!) return 0;

        // A failed earlier event blocks only its aggregate, not unrelated tasks.
        var rows = await db.EventOutbox.FromSqlRaw("""
            SELECT current.* FROM identity_event_outbox current
            WHERE current.published_at IS NULL AND current.next_attempt_at <= now()
              AND NOT EXISTS (
                SELECT 1 FROM identity_event_outbox earlier
                WHERE earlier.aggregate_id = current.aggregate_id
                  AND earlier.published_at IS NULL AND earlier.sequence < current.sequence)
            ORDER BY current.sequence LIMIT 50 FOR UPDATE OF current
            """).ToListAsync(cancellationToken);
        var delivered = 0;
        foreach (var row in rows)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var message = row.ToMessage();
                await publisher.Publish(message, context => context.MessageId = row.Id, timeout.Token);
                row.MarkPublished(DateTimeOffset.UtcNow);
                delivered++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                row.MarkFailed(DateTimeOffset.UtcNow);
                logger.LogWarning("Identity event {EventId} delivery failed ({ErrorType}), attempt {Attempt}.",
                    row.Id, exception.GetType().Name, row.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return delivered;
    }
}
