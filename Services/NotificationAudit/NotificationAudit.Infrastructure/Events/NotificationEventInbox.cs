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
    public string? SafeEnvelope { get; private set; }

    public bool EnsureSameContext(string? safeEnvelope)
    {
        if (SafeEnvelope is not null && safeEnvelope is not null &&
            !System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(SafeEnvelope),
                System.Text.Json.Nodes.JsonNode.Parse(safeEnvelope)))
            throw new NotificationAudit.Application.Events.InvalidNotificationEventException("An event ID was reused with a different source context.");
        if (SafeEnvelope is not null || safeEnvelope is null) return false;
        SafeEnvelope = safeEnvelope;
        return true;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = "PROCESSED";
        ProcessedAt ??= now;
        LastError = null;
    }

    public static NotificationEventInbox Create(
        Guid eventId,
        string eventType,
        string status,
        DateTimeOffset receivedAt, string? safeEnvelope = null) => new()
        {
            EventId = eventId,
            EventType = eventType,
            Status = status,
            ReceivedAt = receivedAt,
            ProcessedAt = status == "PROCESSED" ? receivedAt : null,
            SafeEnvelope = safeEnvelope
        };
}
