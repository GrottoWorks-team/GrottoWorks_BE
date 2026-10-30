namespace NotificationAudit.Contracts.Notifications;

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
