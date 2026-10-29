namespace Reporting.Contracts.Events;

/// <summary>
/// <c>recognition.entry.created</c> (API Contract v1.3.2 §8) — publisher Reporting (Vũ), consumer NotificationAudit
/// (tell the volunteer). One event per recognition point; <see cref="RuleVersion"/> is the exact version applied (BR-68).
/// </summary>
public sealed record RecognitionEntryCreated(
    Guid RecognitionPointId,
    Guid AwardedToUserId,
    Guid SeasonId,
    Guid ParishId,
    Guid ContributionId,
    string ContributionTypeCode,
    Guid RecognitionRuleId,
    string RuleName,
    int RuleVersion,
    int Points,
    DateTimeOffset CreatedAt)
{
    public const string EventType = "recognition.entry.created";
    public const int Version = 1;
}
