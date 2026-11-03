using System.Text.Json;

namespace ParishCoordination.Contracts.Events;

public sealed record SeasonScopeSnapshot(Guid SeasonId, Guid ParishId, DateOnly StartDate,
    DateOnly EndDate, string Status, long Version);

public sealed record WorkAreaScopeSnapshot(Guid WorkAreaId, Guid SeasonId, Guid ParishId,
    string Name, Guid? CommunityId, Guid? CategoryId, Guid? LeaderUserId, Guid? MaterialOfficerUserId,
    DateOnly? StartDate, DateOnly? Deadline, string Status, long Version,
    decimal? Latitude, decimal? Longitude);

/// <summary>Technical scope replication, independent of the business event catalog.</summary>
public sealed record PlanningScopeSnapshot(Guid SnapshotId, string ScopeType, Guid AggregateId,
    long SourceVersion, DateTimeOffset CapturedAt, SeasonScopeSnapshot Season, WorkAreaScopeSnapshot? WorkArea);

public sealed record PlanningIntegrationEvent(Guid EventId, string EventType, int Version,
    DateTimeOffset OccurredAt, string CorrelationId, Guid? ActorId, Guid ParishId, Guid SeasonId,
    string AggregateType, Guid AggregateId, long AggregateVersion, JsonElement Payload);
