using System.Text.Json;

namespace NotificationAudit.Contracts.Audit;

public sealed record ActivityLogDto(
    Guid Id,
    Guid? ActorUserId,
    Guid? ParishId,
    Guid? SeasonId,
    string Action,
    string EntityType,
    Guid EntityId,
    JsonElement? OldValue,
    JsonElement? NewValue,
    string? Reason,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt);

public sealed record StateChangeDto(
    Guid LogId,
    string Action,
    JsonElement? OldValue,
    JsonElement? NewValue,
    string? Reason,
    Guid? ActorUserId,
    DateTimeOffset ChangedAt);
