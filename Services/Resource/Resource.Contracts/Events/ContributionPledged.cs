namespace Resource.Contracts.Events;

/// <summary>One promised material line.</summary>
public sealed record ContributionPledgeLine(Guid DonationItemId, Guid MaterialId, decimal PledgedQuantity);

/// <summary>
/// <c>contribution.pledged</c> (API Contract v1.3.2 §8) — publisher Resource (Vũ), consumers Reporting and
/// NotificationAudit (BA 8.7 step 4: the material officer is told about a new pledge). A pledge never counts as
/// stock or money received (BR-39). <see cref="MaterialOfficerUserId"/> is the officer of the receiving work area, if any.
/// </summary>
public sealed record ContributionPledged(
    Guid DonationId,
    Guid DonorId,
    Guid DonorUserId,
    Guid ParishId,
    Guid SeasonId,
    Guid? WorkAreaId,
    Guid? MaterialOfficerUserId,
    string DonationType,
    decimal? PledgedAmount,
    string Currency,
    IReadOnlyList<ContributionPledgeLine> Items,
    DateTimeOffset PledgedAt)
{
    public const string EventType = "contribution.pledged";

    public const int Version = 1;
}
