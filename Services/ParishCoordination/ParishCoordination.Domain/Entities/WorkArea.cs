using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Domain.Entities;

public sealed class WorkArea
{
    private WorkArea()
    {
    }

    public static WorkArea Create(
        Guid seasonId,
        Guid? communityId,
        Guid? workAreaCategoryId,
        Guid? leaderUserId,
        Guid? materialOfficerUserId,
        string name,
        string? description,
        DateOnly? startDate,
        DateOnly? deadline,
        WorkAreaStatus status,
        DateTimeOffset createdAtUtc,
        decimal? latitude = null,
        decimal? longitude = null)
    {
        return Create(
            Guid.NewGuid(),
            seasonId,
            communityId,
            workAreaCategoryId,
            leaderUserId,
            materialOfficerUserId,
            name,
            description,
            startDate,
            deadline,
            status,
            createdAtUtc, latitude, longitude);
    }

    public static WorkArea Create(
        Guid id,
        Guid seasonId,
        Guid? communityId,
        Guid? workAreaCategoryId,
        Guid? leaderUserId,
        Guid? materialOfficerUserId,
        string name,
        string? description,
        DateOnly? startDate,
        DateOnly? deadline,
        WorkAreaStatus status,
        DateTimeOffset createdAtUtc,
        decimal? latitude = null,
        decimal? longitude = null)
    {
        ValidateCoordinates(latitude, longitude);
        return new WorkArea
        {
            Id = id,
            SeasonId = seasonId,
            CommunityId = communityId,
            WorkAreaCategoryId = workAreaCategoryId,
            LeaderUserId = leaderUserId,
            MaterialOfficerUserId = materialOfficerUserId,
            Name = name,
            Description = description,
            StartDate = startDate,
            Deadline = deadline,
            Status = status,
            CreatedAt = createdAtUtc,
            Version = 1,
            Latitude = latitude,
            Longitude = longitude
        };
    }

    public void Update(
        string name,
        string? description,
        Guid? workAreaCategoryId,
        DateOnly? startDate,
        DateOnly? deadline,
        DateTimeOffset updatedAtUtc,
        bool updateLatitude = false,
        decimal? latitude = null,
        bool updateLongitude = false,
        decimal? longitude = null)
    {
        ValidateCoordinates(latitude, longitude);
        if (updateLatitude) Latitude = latitude;
        if (updateLongitude) Longitude = longitude;
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
        WorkAreaCategoryId = workAreaCategoryId;
        StartDate = startDate;
        Deadline = deadline;
        UpdatedAt = updatedAtUtc;
        Version++;
    }

    public bool AssignCommunity(
        Guid? communityId,
        DateTimeOffset updatedAtUtc)
    {
        if (CommunityId == communityId)
        {
            return false;
        }

        CommunityId = communityId;
        UpdatedAt = updatedAtUtc;
        Version++;
        return true;
    }

    public bool AssignLeader(
        Guid leaderUserId,
        DateTimeOffset updatedAtUtc)
    {
        if (LeaderUserId == leaderUserId)
        {
            return false;
        }

        LeaderUserId = leaderUserId;
        UpdatedAt = updatedAtUtc;
        Version++;
        return true;
    }

    public bool AssignMaterialOfficer(
        Guid materialOfficerUserId,
        DateTimeOffset updatedAtUtc)
    {
        if (MaterialOfficerUserId == materialOfficerUserId)
        {
            return false;
        }

        MaterialOfficerUserId = materialOfficerUserId;
        UpdatedAt = updatedAtUtc;
        Version++;
        return true;
    }

    public Guid Id { get; private set; }

    public bool ReconcileExecution(long eligibleTasks, long completedTasks, bool workStarted,
        bool countsVerified, bool readinessConfirmed, bool readinessReopened, DateTimeOffset now)
    {
        if (eligibleTasks < 0 || completedTasks < 0 || completedTasks > eligibleTasks)
            throw new ArgumentOutOfRangeException(nameof(completedTasks));
        if (Status == WorkAreaStatus.Completed) return false;

        var allCompleted = countsVerified && eligibleTasks > 0 && completedTasks == eligibleTasks;
        var target = Status;
        if (readinessReopened)
            target = WorkAreaStatus.NeedsCorrection;
        else if (allCompleted && (workStarted || Status != WorkAreaStatus.Planned))
            target = readinessConfirmed ? WorkAreaStatus.Ready : WorkAreaStatus.ReadinessReview;
        else if (workStarted || Status is WorkAreaStatus.Ready or WorkAreaStatus.ReadinessReview)
            target = WorkAreaStatus.InProgress;

        if (Status == target) return false;
        Status = target;
        UpdatedAt = now;
        Version++;
        return true;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != WorkAreaStatus.Ready)
            throw new InvalidOperationException("Only a READY Work Area can be completed.");
        Status = WorkAreaStatus.Completed;
        UpdatedAt = now;
        Version++;
    }

    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }

    public static void ValidateCoordinates(decimal? latitude, decimal? longitude)
    {
        if (latitude is < -90m or > 90m)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        if (longitude is < -180m or > 180m)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
    }

    public Guid SeasonId { get; private set; }

    public Guid? CommunityId { get; private set; }

    public Guid? WorkAreaCategoryId { get; private set; }

    public Guid? LeaderUserId { get; private set; }

    public Guid? MaterialOfficerUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? Deadline { get; private set; }

    public WorkAreaStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public int Version { get; private set; } = 1;
}
