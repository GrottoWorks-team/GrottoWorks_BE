namespace NotificationAudit.Domain.Notifications;

public sealed class NotificationRule
{
    private NotificationRule() { }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public bool InAppEnabled { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static NotificationRule Create(Guid id, string eventType, DateTimeOffset now) => new()
    {
        Id = id,
        EventType = eventType,
        InAppEnabled = true,
        Version = 1,
        UpdatedAt = now
    };

    public bool Update(bool inAppEnabled, DateTimeOffset now)
    {
        if (InAppEnabled == inAppEnabled) return false;
        InAppEnabled = inAppEnabled;
        UpdatedAt = now;
        Version++;
        return true;
    }
}
