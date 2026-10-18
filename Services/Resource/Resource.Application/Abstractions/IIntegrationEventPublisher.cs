namespace Resource.Application.Abstractions;

/// <summary>
/// Publishes integration events (API Contract section 8). Must write through the transactional outbox
/// once <c>BuildingBlocks.Messaging</c> ships the outbox (F-PLT-05); until then events are only logged.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TPayload>(
        string eventType,
        int version,
        TPayload payload,
        string aggregateType,
        Guid aggregateId,
        CancellationToken cancellationToken = default)
        where TPayload : class;
}
