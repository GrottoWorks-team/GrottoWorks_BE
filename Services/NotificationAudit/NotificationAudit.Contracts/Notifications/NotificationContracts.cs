namespace NotificationAudit.Contracts.Notifications;

using System.ComponentModel.DataAnnotations;

public sealed record NotificationDto(
    Guid Id,
    string EventType,
    string Title,
    string Body,
    string? ReferenceType,
    Guid? ReferenceId,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationPreferenceDto(
    bool InAppEnabled,
    bool PushEnabled,
    bool EmailEnabled);

public sealed record UpdateNotificationPreferenceRequest(
    [property: Required] bool? InAppEnabled,
    [property: Required] bool? PushEnabled,
    [property: Required] bool? EmailEnabled);

public sealed record UpdateNotificationRuleRequest([property: Required] bool? InAppEnabled);

public sealed record NotificationRuleDto(
    Guid Id,
    string EventType,
    bool InAppEnabled,
    int Version,
    DateTimeOffset UpdatedAt);
