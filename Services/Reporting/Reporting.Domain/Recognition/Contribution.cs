namespace Reporting.Domain.Recognition;

/// <summary>
/// <c>contribution</c> (ERD v3.6.2, F-RPT-02, BR-54): a confirmed business event that counts as a contribution. Donation
/// rows are one summary per donation (created on the first receipt, upserted on later ones — <c>donation_id</c> unique,
/// no user, no reference); every other row credits one user for one reference (task, attendance, purchase, support
/// member), unique per (reference, user) so the same event is never counted twice (BR-55).
/// Technical columns: <c>parish_id</c> (tenancy, BR-57/67), <c>work_area_id</c> (reports) and <c>donor_user_id</c>
/// (account linked to the donor, who receives the points — external donors have none).
/// </summary>
public sealed class Contribution
{
    public const int MaxDescriptionLength = 2000;

    private Contribution()
    {
    }

    public Guid ContributionId { get; private set; }

    public Guid? ContributedByUserId { get; private set; }

    public Guid? DonationId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid? WorkAreaId { get; private set; }

    public Guid ContributionTypeId { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset ContributionDate { get; private set; }

    public string? ReferenceType { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public Guid? DonorUserId { get; private set; }

    /// <summary>The account credited with the contribution (and its points), if any.</summary>
    public Guid? CreditedUserId => ContributedByUserId ?? DonorUserId;

    public static Contribution ForUser(
        Guid userId,
        Guid seasonId,
        Guid parishId,
        Guid? workAreaId,
        Guid contributionTypeId,
        string referenceType,
        Guid referenceId,
        string? description,
        DateTimeOffset date) =>
        new()
        {
            ContributionId = Guid.NewGuid(),
            ContributedByUserId = userId,
            SeasonId = seasonId,
            ParishId = parishId,
            WorkAreaId = workAreaId,
            ContributionTypeId = contributionTypeId,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = Trim(description),
            ContributionDate = date.ToUniversalTime()
        };

    public static Contribution ForDonation(
        Guid donationId,
        Guid? donorUserId,
        Guid seasonId,
        Guid parishId,
        Guid? workAreaId,
        Guid contributionTypeId,
        string? description,
        DateTimeOffset date) =>
        new()
        {
            ContributionId = Guid.NewGuid(),
            DonationId = donationId,
            DonorUserId = donorUserId,
            SeasonId = seasonId,
            ParishId = parishId,
            WorkAreaId = workAreaId,
            ContributionTypeId = contributionTypeId,
            Description = Trim(description),
            ContributionDate = date.ToUniversalTime()
        };

    /// <summary>A later receipt or a correction of the same donation: keeps the first date, refreshes the summary.</summary>
    public void UpdateDonation(Guid? donorUserId, string? description)
    {
        DonorUserId = donorUserId ?? DonorUserId;
        Description = Trim(description) ?? Description;
    }

    /// <summary>The same reference reported again (e.g. corrected approved hours): only the description changes.</summary>
    public void Describe(string? description) => Description = Trim(description) ?? Description;

    private static string? Trim(string? value)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return cleaned is { Length: > MaxDescriptionLength } ? cleaned[..MaxDescriptionLength] : cleaned;
    }
}
