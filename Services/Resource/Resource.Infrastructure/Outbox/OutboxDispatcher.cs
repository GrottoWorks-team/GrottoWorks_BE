using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Outbox;

/// <summary>
/// Publishes pending outbox rows to RabbitMQ as <c>ResourceIntegrationEvent</c> (enabled by <c>Outbox:DispatchEnabled</c>). Rows are
/// claimed with <c>FOR UPDATE SKIP LOCKED</c> so several instances never publish the same event, sent in creation order, and a
/// batch stops at the first failure (kept for a backed-off retry) so events of one aggregate are not reordered.
/// Delivery is at-least-once; consumers de-duplicate by <c>eventId</c> (= message id).
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const int BatchSize = 50;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) == BatchSize)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch iteration failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <returns>How many rows were published.</returns>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ResourceDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var rows = await dbContext.Set<OutboxMessage>()
            .FromSqlRaw(
                $"SELECT * FROM {OutboxMessageConfiguration.TableName} WHERE published_at IS NULL AND next_attempt_at <= now() " +
                $"ORDER BY created_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        var published = 0;
        foreach (var row in rows)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(PublishTimeout);
                await publisher.Publish(row.ToMessage(), context => context.MessageId = row.EventId, timeout.Token);
                row.MarkPublished(DateTimeOffset.UtcNow);
                published++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Publishing outbox event {EventId} ({EventType}) failed.", row.EventId, row.EventType);
                row.MarkFailed(exception.Message, DateTimeOffset.UtcNow);
                break;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return published;
    }
}
