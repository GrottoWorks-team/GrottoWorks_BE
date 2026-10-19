namespace TaskExecution.Domain.Scopes;

public enum SeasonScopeStatus
{
    Planning,
    Active,
    Ready,
    Completed,
    Archived
}

public sealed class SeasonScope
{
    private SeasonScope()
    {
    }

    public Guid SeasonId { get; private set; }
    public Guid ParishId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public SeasonScopeStatus Status { get; private set; }
    public long SourceVersion { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsReadOnly => Status == SeasonScopeStatus.Archived;

    public static SeasonScope Create(
        Guid seasonId,
        Guid parishId,
        DateOnly startDate,
        DateOnly endDate,
        SeasonScopeStatus status,
        long sourceVersion,
        DateTimeOffset updatedAt)
    {
        Validate(seasonId, parishId, startDate, endDate, sourceVersion);

        return new SeasonScope
        {
            SeasonId = seasonId,
            ParishId = parishId,
            StartDate = startDate,
            EndDate = endDate,
            Status = status,
            SourceVersion = sourceVersion,
            UpdatedAt = updatedAt.ToUniversalTime()
        };
    }

    public bool Apply(
        Guid parishId,
        DateOnly startDate,
        DateOnly endDate,
        SeasonScopeStatus status,
        long sourceVersion,
        DateTimeOffset updatedAt)
    {
        Validate(SeasonId, parishId, startDate, endDate, sourceVersion);
        if (sourceVersion <= SourceVersion)
        {
            return false;
        }

        ParishId = parishId;
        StartDate = startDate;
        EndDate = endDate;
        Status = status;
        SourceVersion = sourceVersion;
        UpdatedAt = updatedAt.ToUniversalTime();
        return true;
    }

    private static void Validate(
        Guid seasonId,
        Guid parishId,
        DateOnly startDate,
        DateOnly endDate,
        long sourceVersion)
    {
        if (seasonId == Guid.Empty)
        {
            throw DomainRuleException.Validation("SEASON_ID_REQUIRED", "Season ID is required.");
        }

        if (parishId == Guid.Empty)
        {
            throw DomainRuleException.Validation("PARISH_ID_REQUIRED", "Parish ID is required.");
        }

        if (endDate <= startDate)
        {
            throw DomainRuleException.Validation("SEASON_DATE_RANGE_INVALID", "Season end date must be after its start date.");
        }

        if (sourceVersion < 1)
        {
            throw DomainRuleException.Validation("SOURCE_VERSION_INVALID", "Source version must be a positive number.");
        }
    }
}
