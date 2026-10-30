namespace NotificationAudit.Domain.Notifications;

public sealed class Notification
{
    private Notification() { }

    public Guid Id { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string? ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(
        Guid recipientUserId,
        string eventType,
        string title,
        string body,
        string? referenceType,
        Guid? referenceId,
        DateTimeOffset createdAt) => new()
        {
            Id = Guid.NewGuid(),
            RecipientUserId = recipientUserId,
            EventType = eventType,
            Title = title,
            Body = body,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            CreatedAt = createdAt
        };

    public bool MarkRead(DateTimeOffset readAt)
    {
        if (IsRead) return false;
        IsRead = true;
        ReadAt = readAt;
        return true;
    }
}
