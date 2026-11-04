using System.Text.Json;

namespace Identity.Contracts.Events;

public sealed record IdentityIntegrationEvent(
    Guid EventId, string EventType, int Version, DateTimeOffset OccurredAt,
    string CorrelationId, Guid? ActorId, Guid? ParishId, Guid? SeasonId,
    string AggregateType, Guid AggregateId, long AggregateVersion, JsonElement Payload);
