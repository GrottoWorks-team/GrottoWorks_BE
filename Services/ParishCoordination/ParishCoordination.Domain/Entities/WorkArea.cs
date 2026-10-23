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
        DateTimeOffset createdAtUtc)
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
            createdAtUtc);
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
        DateTimeOffset createdAtUtc)
    {
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
            Version = 1
        };
    }

    public void Update(
        string name,
        string? description,
        Guid? workAreaCategoryId,
        DateOnly? startDate,
        DateOnly? deadline,
        DateTimeOffset updatedAtUtc)
    {
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
