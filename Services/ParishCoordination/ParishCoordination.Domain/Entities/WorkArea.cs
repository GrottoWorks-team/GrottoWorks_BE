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
            CreatedAt = createdAtUtc
        };
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
}