using BuildingBlocks.Security;
using ParishCoordination.Application.Parishes;

namespace ParishCoordination.Application.Communities.Queries.GetCommunities;

public sealed class GetCommunitiesQueryHandler(
    IParishRepository parishRepository,
    ICommunityRepository communityRepository,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<CommunityDto>?> HandleAsync(
        GetCommunitiesQuery query,
        CancellationToken cancellationToken = default)
    {
        var parishExists = await parishRepository.ExistsAsync(
            query.ParishId,
            cancellationToken);

        if (!parishExists)
        {
            return null;
        }

        ParishAccess.EnsureSameParish(currentUser, query.ParishId);

        var communities = await communityRepository.GetByParishIdAsync(
            query.ParishId,
            cancellationToken);

        return communities
            .Select(CommunityDto.From)
            .OrderBy(community => community.Name)
            .ThenBy(community => community.Id)
            .ToList();
    }
}
