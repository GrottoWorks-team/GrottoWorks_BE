namespace TaskExecution.Domain.Scopes;

public enum WorkAreaScopeStatus
{
    Planned,
    InProgress,
    ReadinessReview,
    NeedsCorrection,
    Ready,
    Completed
}

public sealed class WorkAreaScope
{
    private WorkAreaScope()
    {
    }

    public Guid WorkAreaId { get; private set; }
    public Guid SeasonId { get; private set; }
    public Guid ParishId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid? CommunityId { get; private set; }
    public Guid? LeaderUserId { get; private set; }
    public Guid? MaterialOfficerUserId { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? Deadline { get; private set; }
    public WorkAreaScopeStatus Status { get; private set; }
    public long SourceVersion { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static WorkAreaScope Create(
        Guid workAreaId,
        Guid seasonId,
        Guid parishId,
        string name,
        Guid? communityId,
        Guid? leaderUserId,
        Guid? materialOfficerUserId,
        DateOnly? startDate,
        DateOnly? deadline,
        WorkAreaScopeStatus status,
        long sourceVersion,
        DateTimeOffset updatedAt)
    {
        var normalizedName = ValidateAndNormalize(
            workAreaId, seasonId, parishId, name, startDate, deadline, sourceVersion);

        return new WorkAreaScope
        {
            WorkAreaId = workAreaId,
            SeasonId = seasonId,
            ParishId = parishId,
            Name = normalizedName,
            CommunityId = communityId,
            LeaderUserId = leaderUserId,
            MaterialOfficerUserId = materialOfficerUserId,
            StartDate = startDate,
            Deadline = deadline,
            Status = status,
            SourceVersion = sourceVersion,
            UpdatedAt = updatedAt.ToUniversalTime()
        };
    }

    public bool Apply(
        Guid seasonId,
        Guid parishId,
        string name,
        Guid? communityId,
        Guid? leaderUserId,
        Guid? materialOfficerUserId,
        DateOnly? startDate,
        DateOnly? deadline,
        WorkAreaScopeStatus status,
        long sourceVersion,
        DateTimeOffset updatedAt)
    {
        var normalizedName = ValidateAndNormalize(
            WorkAreaId, seasonId, parishId, name, startDate, deadline, sourceVersion);
        if (sourceVersion <= SourceVersion)
        {
            return false;
        }

        SeasonId = seasonId;
        ParishId = parishId;
        Name = normalizedName;
        CommunityId = communityId;
        LeaderUserId = leaderUserId;
        MaterialOfficerUserId = materialOfficerUserId;
        StartDate = startDate;
        Deadline = deadline;
        Status = status;
        SourceVersion = sourceVersion;
        UpdatedAt = updatedAt.ToUniversalTime();
        return true;
    }

    private static string ValidateAndNormalize(
        Guid workAreaId,
        Guid seasonId,
        Guid parishId,
        string name,
        DateOnly? startDate,
        DateOnly? deadline,
        long sourceVersion)
    {
        if (workAreaId == Guid.Empty)
        {
            throw DomainRuleException.Validation("WORK_AREA_ID_REQUIRED", "Work area ID is required.");
        }

        if (seasonId == Guid.Empty || parishId == Guid.Empty)
        {
            throw DomainRuleException.Validation("WORK_AREA_SCOPE_INVALID", "Season ID and parish ID are required.");
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0 || normalizedName.Length > 150)
        {
            throw DomainRuleException.Validation("WORK_AREA_NAME_INVALID", "Work area name must contain 1 to 150 characters.");
        }

        if (startDate.HasValue && deadline.HasValue && deadline.Value < startDate.Value)
        {
            throw DomainRuleException.Validation("WORK_AREA_DATE_RANGE_INVALID", "Work area deadline cannot be before its start date.");
        }

        if (sourceVersion < 1)
        {
            throw DomainRuleException.Validation("SOURCE_VERSION_INVALID", "Source version must be a positive number.");
        }

        return normalizedName;
    }
}
