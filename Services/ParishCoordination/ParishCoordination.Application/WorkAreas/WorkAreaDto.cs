using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreas;

public sealed record WorkAreaDto(
    Guid Id,
    Guid SeasonId,
    string Name,
    string? Description,
    Guid? CommunityId,
    Guid? CategoryId,
    Guid? LeaderId,
    Guid? MaterialOfficerId,
    DateOnly? StartDate,
    DateOnly? Deadline,
    WorkAreaStatus Status,
    int Version)
{
    public static WorkAreaDto From(WorkArea workArea)
    {
        return new WorkAreaDto(
            workArea.Id,
            workArea.SeasonId,
            workArea.Name,
            workArea.Description,
            workArea.CommunityId,
            workArea.WorkAreaCategoryId,
            workArea.LeaderUserId,
            workArea.MaterialOfficerUserId,
            workArea.StartDate,
            workArea.Deadline,
            workArea.Status,
            workArea.Version);
    }
}
