namespace Identity.Contracts.Events;

/// <summary>
/// <c>identity.user.locked</c> — an administrator locked an account (spec §6.1).
/// Publisher: Identity (Vũ). Consumers: Task, Parish, NotificationAudit
/// (Task blocks assigning a locked user — edge case #24).
/// Payload frozen in advance so consumers can be written before F-IDN-08 ships.
/// </summary>
public sealed record IdentityUserLocked(
    Guid UserId,
    Guid? ParishId,
    string? Reason,
    Guid? LockedBy,
    DateTimeOffset OccurredAt)
{
    public const string EventType = "identity.user.locked";

    public const int Version = 1;
}
