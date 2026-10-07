using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Communities;

public sealed record CommunityDto(
    Guid Id,
    Guid ParishId,
    string Name,
    string? Description,
    CommunityStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int Version)
{
    public static CommunityDto From(Community community)
    {
        return new CommunityDto(
            community.Id,
            community.ParishId,
            community.Name,
            community.Description,
            community.Status,
            community.CreatedAt,
            community.UpdatedAt,
            community.Version);
    }
}
