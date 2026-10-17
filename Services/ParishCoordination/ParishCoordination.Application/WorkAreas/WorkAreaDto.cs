using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreas;

public sealed record WorkAreaDto(
    Guid Id,
    Guid SeasonId,
    Guid? CommunityId,
    Guid? LeaderId,
    Guid? MaterialOfficerId,
    WorkAreaStatus Status,
    int Version)
{
    public static WorkAreaDto From(WorkArea workArea)
    {
        return new WorkAreaDto(
            workArea.Id,
            workArea.SeasonId,
            workArea.CommunityId,
            workArea.LeaderUserId,
            workArea.MaterialOfficerUserId,
            workArea.Status,
            workArea.Version);
    }
}
