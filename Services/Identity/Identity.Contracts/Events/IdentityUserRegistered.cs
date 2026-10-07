namespace Identity.Contracts.Events;

/// <summary>
/// <c>identity.user.registered</c> — a self-registration completed (spec §6.1).
/// Publisher: Identity (Vũ). Consumers: NotificationAudit.
/// Contract-first: the record is merged before the publishing infrastructure lands, so consumers can
/// code against it with sample data. Changing the payload requires bumping <see cref="Version"/>.
/// </summary>
public sealed record IdentityUserRegistered(
    Guid UserId,
    string Email,
    string FullName,
    Guid ParishId,
    string Role,
    DateTimeOffset OccurredAt)
{
    public const string EventType = "identity.user.registered";

    public const int Version = 1;
}
