namespace Reporting.Domain.Recognition;

/// <summary><c>recognition_rule.rule_status</c>.</summary>
public enum RecognitionRuleStatus
{
    Active,
    Inactive
}

/// <summary>
/// <c>recognition_rule</c> (ERD v3.6.2, F-RPT-01, BA 13, BR-68): points awarded per contribution of one type during an
/// effective period. Rules are never edited: changing points creates the next version of the same rule (same name),
/// whose start ends the previous version the day before, so versions of one rule never overlap. Deactivation ends a
/// version; inactive rules award no new points, points already awarded keep their exact rule version.
/// </summary>
public sealed class RecognitionRule
{
    public const int MaxNameLength = 150;
    public const int MaxDescriptionLength = 2000;
    public const int MaxPoints = 10_000;

    private RecognitionRule()
    {
    }

    public Guid RecognitionRuleId { get; private set; }

    public Guid ContributionTypeId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int RuleVersion { get; private set; }

    public int Points { get; private set; }

    public string? Description { get; private set; }

    public RecognitionRuleStatus Status { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public static RecognitionRule Create(
        Guid contributionTypeId,
        string? name,
        int points,
        string? description,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        var rule = new RecognitionRule
        {
            RecognitionRuleId = Guid.NewGuid(),
            ContributionTypeId = contributionTypeId,
            Name = Text.Required(name, MaxNameLength, "RECOGNITION_RULE_NAME_INVALID", "Rule name"),
            RuleVersion = 1,
            Status = RecognitionRuleStatus.Active
        };
        rule.Set(points, description, effectiveFrom, effectiveTo);
        return rule;
    }

    /// <summary>BR-68: the next version of this rule; this version ends the day before the new one starts.</summary>
    public RecognitionRule NextVersion(int points, string? description, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (Status != RecognitionRuleStatus.Active)
        {
            throw new DomainRuleException("RECOGNITION_RULE_INACTIVE", "Only an active rule version gets a new version.");
        }

        if (effectiveFrom <= EffectiveFrom)
        {
            throw DomainRuleException.Validation(
                "RECOGNITION_RULE_PERIOD_INVALID",
                $"The new version must start after {EffectiveFrom:yyyy-MM-dd}, when the current version starts.");
        }

        var next = new RecognitionRule
        {
            RecognitionRuleId = Guid.NewGuid(),
            ContributionTypeId = ContributionTypeId,
            Name = Name,
            RuleVersion = RuleVersion + 1,
            Status = RecognitionRuleStatus.Active
        };
        next.Set(points, description ?? Description, effectiveFrom, effectiveTo);
        if (EffectiveTo is null || EffectiveTo >= effectiveFrom)
        {
            EffectiveTo = effectiveFrom.AddDays(-1);
        }

        return next;
    }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "RECOGNITION_RULE_VERSION_MISMATCH",
                "The rule was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    /// <summary>Ends this version today (or cancels a version that has not started yet).</summary>
    public void Deactivate(string? reason, DateOnly today)
    {
        Text.Required(reason, 500, "RECOGNITION_RULE_REASON_REQUIRED", "Reason");
        if (Status != RecognitionRuleStatus.Active)
        {
            throw new DomainRuleException("RECOGNITION_RULE_INACTIVE", "The rule version is already inactive.");
        }

        Status = RecognitionRuleStatus.Inactive;
        var end = today < EffectiveFrom ? EffectiveFrom : today;
        if (EffectiveTo is null || EffectiveTo > end)
        {
            EffectiveTo = end;
        }
    }

    /// <summary>Whether this version awards points for a contribution dated <paramref name="date"/>.</summary>
    public bool AppliesOn(DateOnly date) =>
        Status == RecognitionRuleStatus.Active && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);

    /// <summary>Whether the effective periods of two rule versions share at least one day.</summary>
    public bool Overlaps(RecognitionRule other) =>
        (EffectiveTo is null || other.EffectiveFrom <= EffectiveTo) && (other.EffectiveTo is null || EffectiveFrom <= other.EffectiveTo);

    private void Set(int points, string? description, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (points is < 1 or > MaxPoints)
        {
            throw DomainRuleException.Validation("RECOGNITION_RULE_POINTS_INVALID", $"Points must be between 1 and {MaxPoints} (BR-56: no cash value).");
        }

        if (effectiveTo is { } end && end < effectiveFrom)
        {
            throw DomainRuleException.Validation("RECOGNITION_RULE_PERIOD_INVALID", "effectiveTo must be on or after effectiveFrom.");
        }

        Points = points;
        Description = Text.Optional(description, MaxDescriptionLength, "Description");
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }
}
