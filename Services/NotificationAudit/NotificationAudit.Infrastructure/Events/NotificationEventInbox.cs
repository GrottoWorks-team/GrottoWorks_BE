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
        if (SafeEnvelope is not null && safeEnvelope is not null)
        {
            var previous = System.Text.Json.Nodes.JsonNode.Parse(SafeEnvelope);
            var incoming = System.Text.Json.Nodes.JsonNode.Parse(safeEnvelope);
            if (previous is System.Text.Json.Nodes.JsonObject before && incoming is System.Text.Json.Nodes.JsonObject after &&
                before["payload"] is System.Text.Json.Nodes.JsonObject oldPayload && after["payload"] is System.Text.Json.Nodes.JsonObject newPayload)
            {
                // Redaction may learn additional safe fields; never rewrite the original context or change known facts.
                var beforeMetadata = (System.Text.Json.Nodes.JsonObject)before.DeepClone();
                var afterMetadata = (System.Text.Json.Nodes.JsonObject)after.DeepClone();
                beforeMetadata.Remove("payload");
                afterMetadata.Remove("payload");
                if (!System.Text.Json.Nodes.JsonNode.DeepEquals(beforeMetadata, afterMetadata) ||
                    oldPayload.Any(field => !newPayload.TryGetPropertyValue(field.Key, out var value) ||
                        !System.Text.Json.Nodes.JsonNode.DeepEquals(field.Value, value)))
                    throw ContextConflict();
            }
            else if (!System.Text.Json.Nodes.JsonNode.DeepEquals(previous, incoming)) throw ContextConflict();
        }
        if (SafeEnvelope is not null || safeEnvelope is null) return false;
        SafeEnvelope = safeEnvelope;
        return true;
    }

    private static NotificationAudit.Application.Events.InvalidNotificationEventException ContextConflict() =>
        new("An event ID was reused with a different source context.");

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
