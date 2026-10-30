using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.TaskScopeSync;

public sealed class TaskScopeSyncWorker(IServiceScopeFactory scopes, TaskScopeSyncOptions options,
    ILogger<TaskScopeSyncWorker> logger) : BackgroundService
{
    private const long DispatcherLock = 710301;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backfilled = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ParishCoordinationDbContext>();
                await db.Database.OpenConnectionAsync(stoppingToken);
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"SELECT pg_try_advisory_lock({DispatcherLock})";
                var acquired = (bool)(await command.ExecuteScalarAsync(stoppingToken))!;
                if (acquired)
                {
                    try
                    {
                        if (!backfilled)
                        {
                            await scope.ServiceProvider.GetRequiredService<TaskScopeBackfill>()
                                .EnqueueCurrentAsync(stoppingToken);
                            backfilled = true;
                            logger.LogInformation("Task scope startup backfill enqueued.");
                        }

                        await DispatchBatchAsync(db,
                            scope.ServiceProvider.GetRequiredService<ITaskScopeDelivery>(), stoppingToken);
                    }
                    finally
                    {
                        command.CommandText = $"SELECT pg_advisory_unlock({DispatcherLock})";
                        await command.ExecuteScalarAsync(CancellationToken.None);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Task scope worker iteration failed ({ErrorType}); retrying.", exception.GetType().Name);
            }

            try { await Task.Delay(options.PollInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task DispatchBatchAsync(ParishCoordinationDbContext db, ITaskScopeDelivery delivery,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var batch = await db.TaskScopeOutbox.AsNoTracking()
            .Where(row => row.DeliveredAt == null && row.NextAttemptAt <= now)
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Take(50).ToListAsync(cancellationToken);
        foreach (var message in batch)
        {
            try
            {
                await delivery.DeliverAsync(message, cancellationToken);
                var deliveredAt = DateTimeOffset.UtcNow;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE task_scope_outbox SET delivered_at = {deliveredAt}, attempts = attempts + 1,
                        last_error = NULL WHERE id = {message.Id}
                    """, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var error = exception is HttpRequestException { StatusCode: { } status }
                    ? $"HTTP {(int)status}" : exception.GetType().Name;
                var nextAttempt = DateTimeOffset.UtcNow + TaskScopeSyncOptions.RetryDelay(message.Attempts + 1);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE task_scope_outbox SET attempts = attempts + 1, next_attempt_at = {nextAttempt},
                        last_error = {error} WHERE id = {message.Id}
                    """, cancellationToken);
                logger.LogWarning("Task scope {ScopeType}/{ScopeId} v{Version} failed ({Error}); scheduled retry.",
                    message.ScopeType, message.AggregateId, message.SourceVersion, error);
            }
        }
    }
}
