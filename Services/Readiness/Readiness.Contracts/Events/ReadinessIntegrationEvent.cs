using System.Text.Json;

namespace Readiness.Contracts.Events;

/// <summary>
/// Broker message of every Readiness integration event (transactional outbox → RabbitMQ), same envelope shape as TaskExecution's
/// <c>TaskIntegrationEvent</c>: <see cref="Payload"/> is the record named by <see cref="EventType"/> / <see cref="Version"/>
/// (camelCase JSON), <see cref="EventId"/> is also the broker message id so consumers can de-duplicate.
/// </summary>
public sealed record ReadinessIntegrationEvent(
    Guid EventId,
    string EventType,
    int Version,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    Guid? ActorId,
    Guid? ParishId,
    Guid? SeasonId,
    string AggregateType,
    Guid AggregateId,
    JsonElement Payload);
