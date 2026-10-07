namespace Identity.Application.Abstractions;

/// <summary>
/// Publishes integration events (API Contract section 8 envelope) to the messaging infrastructure.
/// The implementation must write through the transactional outbox together with the business change
/// once <c>BuildingBlocks.Messaging</c> (F-PLT-05) is available; until then events are logged only.
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
