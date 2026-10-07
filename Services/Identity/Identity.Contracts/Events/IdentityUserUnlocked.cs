namespace Identity.Contracts.Events;

/// <summary>
/// <c>identity.user.unlocked</c> — an administrator unlocked an account (spec §6.1).
/// Publisher: Identity (Vũ). Consumers: Task, Parish, NotificationAudit.
/// </summary>
public sealed record IdentityUserUnlocked(
    Guid UserId,
    Guid? ParishId,
    Guid? UnlockedBy,
    DateTimeOffset OccurredAt)
{
    public const string EventType = "identity.user.unlocked";

    public const int Version = 1;
}
