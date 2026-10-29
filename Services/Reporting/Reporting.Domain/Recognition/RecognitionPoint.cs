namespace Reporting.Domain.Recognition;

/// <summary>
/// <c>recognition_point</c> (ERD v3.6.2, BR-55, BR-56, BR-68): points of one account for one contribution under the exact
/// rule version applied; (contribution, rule, user) unique so a contribution is never scored twice by the same rule.
/// </summary>
public sealed class RecognitionPoint
{
    private RecognitionPoint()
    {
    }

    public Guid RecognitionPointId { get; private set; }

    public Guid AwardedToUserId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid RecognitionRuleId { get; private set; }

    public Guid ContributionId { get; private set; }

    public int AwardedPoints { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static RecognitionPoint Award(Contribution contribution, Guid userId, RecognitionRule rule, DateTimeOffset now) =>
        new()
        {
            RecognitionPointId = Guid.NewGuid(),
            AwardedToUserId = userId,
            SeasonId = contribution.SeasonId,
            RecognitionRuleId = rule.RecognitionRuleId,
            ContributionId = contribution.ContributionId,
            AwardedPoints = rule.Points,
            Reason = $"{rule.Name} (v{rule.RuleVersion})",
            CreatedAt = now.ToUniversalTime()
        };
}
