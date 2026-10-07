using Identity.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Observability;

/// <summary>
/// Interim publisher: logs the event instead of writing to the outbox.
/// Replaced by the outbox-backed implementation of <c>BuildingBlocks.Messaging</c> (F-PLT-05);
/// until then, events are visible in structured logs and unit tests can assert on them.
/// </summary>
public sealed class LoggingIntegrationEventPublisher(
    ILogger<LoggingIntegrationEventPublisher> logger)
    : IIntegrationEventPublisher
{
    public Task PublishAsync<TPayload>(
        string eventType,
        int version,
        TPayload payload,
        string aggregateType,
        Guid aggregateId,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        logger.LogInformation(
            "Integration event {EventType} v{Version} for {AggregateType} {AggregateId} (outbox pending, F-PLT-05): {@Payload}",
            eventType,
            version,
            aggregateType,
            aggregateId,
            payload);

        return Task.CompletedTask;
    }
}
