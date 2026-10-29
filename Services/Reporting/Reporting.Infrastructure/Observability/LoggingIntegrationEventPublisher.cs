using Microsoft.Extensions.Logging;
using Reporting.Application.Abstractions;

namespace Reporting.Infrastructure.Observability;

/// <summary>
/// Interim publisher: logs the event instead of writing to the outbox (F-PLT-05 has no outbox yet).
/// Replace with the BuildingBlocks.Messaging outbox publisher when it lands — handlers do not change.
/// </summary>
public sealed class LoggingIntegrationEventPublisher(ILogger<LoggingIntegrationEventPublisher> logger)
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
