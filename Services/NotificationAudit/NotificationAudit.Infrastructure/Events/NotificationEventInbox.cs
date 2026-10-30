namespace NotificationAudit.Infrastructure.Events;

public sealed class NotificationEventInbox
{
    private NotificationEventInbox() { }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? LastError { get; private set; }

    public static NotificationEventInbox Create(
        Guid eventId,
        string eventType,
        string status,
        DateTimeOffset receivedAt) => new()
        {
            EventId = eventId,
            EventType = eventType,
            Status = status,
            ReceivedAt = receivedAt,
            ProcessedAt = status == "PROCESSED" ? receivedAt : null
        };
}
