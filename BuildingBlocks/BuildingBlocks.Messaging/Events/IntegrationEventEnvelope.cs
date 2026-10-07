namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Event envelope per BuildingBlocks README §2 (F-PLT-05). The payload is a record declared in
/// <c>&lt;Service&gt;.Contracts.Events</c> with its own <c>EventType</c>/<c>Version</c> constants;
/// bump the record version whenever its shape changes.
/// </summary>
public sealed record IntegrationEventEnvelope(
    string EventType,
    int Version,
    object Payload,
    string AggregateType,
    Guid AggregateId,
    DateTimeOffset OccurredAt)
{
    public static IntegrationEventEnvelope Create<TPayload>(
        string eventType,
        int version,
        TPayload payload,
        string aggregateType,
        Guid aggregateId)
        where TPayload : class =>
        new(eventType, version, payload, aggregateType, aggregateId, DateTimeOffset.UtcNow);
}
