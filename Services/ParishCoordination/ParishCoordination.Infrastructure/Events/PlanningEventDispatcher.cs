using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Events;

public sealed class PlanningEventDispatcher(IServiceScopeFactory scopes, ILogger<PlanningEventDispatcher> logger) : BackgroundService
{
    private const long DispatcherLock = 71110304;

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
                    var db = cleanupScope.ServiceProvider.GetRequiredService<ParishCoordinationDbContext>();
                    var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
                    await db.PlanningEventOutbox.Where(row => row.PublishedAt != null && row.PublishedAt < cutoff)
                        .ExecuteDeleteAsync(stoppingToken);
                    lastCleanup = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Planning event dispatcher failed ({ErrorType}).", exception.GetType().Name);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParishCoordinationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var brokerScopes = scope.ServiceProvider.GetRequiredService<ScopeDeliveryOptions>().UseBroker;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var lockCommand = db.Database.GetDbConnection().CreateCommand();
        lockCommand.Transaction = transaction.GetDbTransaction();
        lockCommand.CommandText = $"SELECT pg_try_advisory_xact_lock({DispatcherLock})";
        if (!(bool)(await lockCommand.ExecuteScalarAsync(cancellationToken))!) return 0;

        // A failed earlier event blocks only its aggregate, not unrelated tasks.
        var rows = await db.PlanningEventOutbox.FromSqlInterpolated($"""
            SELECT current.* FROM planning_event_outbox current
            WHERE current.published_at IS NULL AND current.next_attempt_at <= now()
              AND ({brokerScopes} OR current.event_type <> 'scope.snapshot')
              AND NOT EXISTS (
                SELECT 1 FROM planning_event_outbox earlier
                WHERE earlier.aggregate_id = current.aggregate_id
                  AND ({brokerScopes} OR earlier.event_type <> 'scope.snapshot')
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
                if (row.IsScopeSnapshot)
                    await publisher.Publish(row.ToScopeSnapshot(), context => context.MessageId = row.Id, timeout.Token);
                else
                    await publisher.Publish(row.ToMessage(), context => context.MessageId = row.Id, timeout.Token);
                row.MarkPublished(DateTimeOffset.UtcNow);
                delivered++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                row.MarkFailed(DateTimeOffset.UtcNow);
                logger.LogWarning("Planning event {EventId} delivery failed ({ErrorType}), attempt {Attempt}.",
                    row.Id, exception.GetType().Name, row.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return delivered;
    }
}
