using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Domain.Entities;

public sealed class Season
{
    private Season()
    {
    }

    public static Season Create(
        Guid parishId,
        Guid createdByUserId,
        string name,
        int seasonYear,
        DateOnly startDate,
        DateOnly endDate,
        string? description,
        decimal? estimatedBudget,
        DateTimeOffset createdAtUtc)
    {
        return Create(
            Guid.NewGuid(),
            parishId,
            createdByUserId,
            name,
            seasonYear,
            startDate,
            endDate,
            SeasonStatus.Planning,
            description,
            estimatedBudget,
            createdAtUtc);
    }
    public static Season Create(
        Guid id,
        Guid parishId,
        Guid createdByUserId,
        string name,
        int seasonYear,
        DateOnly startDate,
        DateOnly endDate,
        SeasonStatus status,
        string? description,
        decimal? estimatedBudget,
        DateTimeOffset createdAtUtc)
    {
        return new Season
        {
            Id = id,
            ParishId = parishId,
            CreatedByUserId = createdByUserId,
            Name = name,
            SeasonYear = seasonYear,
            StartDate = startDate,
            EndDate = endDate,
            Status = status,
            Description = description,
            EstimatedBudget = estimatedBudget,
            CreatedAt = createdAtUtc,
            Version = 1
        };
    }

    public void Update(
        string name,
        int seasonYear,
        DateOnly startDate,
        DateOnly endDate,
        string? description,
        decimal? estimatedBudget,
        DateTimeOffset updatedAtUtc)
    {
        Name = name;
        SeasonYear = seasonYear;
        StartDate = startDate;
        EndDate = endDate;
        Description = description;
        EstimatedBudget = estimatedBudget;
        UpdatedAt = updatedAtUtc;
        Version++;
    }

    public void Activate(DateTimeOffset updatedAtUtc)
    {
        if (Status is not SeasonStatus.Planning)
        {
            throw new InvalidOperationException(
                "Only a season in PLANNING status can be activated.");
        }

        Status = SeasonStatus.Active;
        UpdatedAt = updatedAtUtc;
        Version++;
    }

    public void Complete(DateTimeOffset updatedAtUtc)
    {
        if (Status is not SeasonStatus.Ready)
        {
            throw new InvalidOperationException(
                "Only a season in READY status can be completed.");
        }

        Status = SeasonStatus.Completed;
        UpdatedAt = updatedAtUtc;
        Version++;
    }

    public bool ReconcileReadiness(IReadOnlyCollection<WorkAreaStatus> areaStatuses, DateTimeOffset now)
    {
        if (Status is not (SeasonStatus.Active or SeasonStatus.Ready)) return false;
        var target = areaStatuses.Count > 0 && areaStatuses.All(status => status == WorkAreaStatus.Ready)
            ? SeasonStatus.Ready : SeasonStatus.Active;
        if (Status == target) return false;
        Status = target;
        UpdatedAt = now;
        Version++;
        return true;
    }

    public void CompleteWorkAreas(IReadOnlyCollection<WorkArea> areas, DateTimeOffset now)
    {
        if (Status != SeasonStatus.Completed || areas.Count == 0 ||
            areas.Any(area => area.SeasonId != Id || area.Status != WorkAreaStatus.Ready))
            throw new InvalidOperationException("Season completion requires every Work Area to be READY.");
        foreach (var area in areas) area.Complete(now);
    }

    public void Archive(DateTimeOffset updatedAtUtc)
    {
        if (Status is not SeasonStatus.Completed)
        {
            throw new InvalidOperationException(
                "Only a season in COMPLETED status can be archived.");
        }

        Status = SeasonStatus.Archived;
        UpdatedAt = updatedAtUtc;
        Version++;
    }

    public Guid Id { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int SeasonYear { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public SeasonStatus Status { get; private set; }

    public string? Description { get; private set; }

    public decimal? EstimatedBudget { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public int Version { get; private set; } = 1;
}
