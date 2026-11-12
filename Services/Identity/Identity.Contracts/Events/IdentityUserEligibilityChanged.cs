namespace Identity.Contracts.Events;

/// <summary>Signals consumers to read the user's current eligibility from Identity.</summary>
public sealed record IdentityUserEligibilityChanged(Guid UserId)
{
    public const string EventType = "identity.user.eligibility.changed";
    public const int Version = 1;
}
