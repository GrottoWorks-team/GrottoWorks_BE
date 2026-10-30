namespace NotificationAudit.Domain.Notifications;

public sealed class NotificationPreference
{
    private NotificationPreference() { }

    public Guid UserId { get; private set; }
    public bool InAppEnabled { get; private set; }
    public bool PushEnabled { get; private set; }
    public bool EmailEnabled { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static NotificationPreference CreateDefault(Guid userId, DateTimeOffset now) => new()
    {
        UserId = userId,
        InAppEnabled = true,
        PushEnabled = true,
        EmailEnabled = false,
        UpdatedAt = now
    };

    public void Update(bool inAppEnabled, bool pushEnabled, bool emailEnabled, DateTimeOffset now)
    {
        InAppEnabled = inAppEnabled;
        PushEnabled = pushEnabled;
        EmailEnabled = emailEnabled;
        UpdatedAt = now;
    }
}
