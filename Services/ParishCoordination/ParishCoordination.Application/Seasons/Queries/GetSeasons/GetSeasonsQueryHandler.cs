using BuildingBlocks.Security;
using ParishCoordination.Application.Seasons;

namespace ParishCoordination.Application.Seasons.Queries.GetSeasons;

public sealed class GetSeasonsQueryHandler(
    ISeasonRepository seasonRepository,
    ICurrentUser currentUser)
{
    public async Task<GetSeasonsResult> HandleAsync(
        GetSeasonsQuery query,
        CancellationToken cancellationToken = default)
    {
        var parishId = string.Equals(
                currentUser.Role,
                GrottoWorksRoles.Admin,
                StringComparison.Ordinal)
            ? null
            : currentUser.ParishId;

        ParishAccess.EnsureSameParish(currentUser, parishId);

        var (seasons, totalItems) = await seasonRepository.GetPagedAsync(
            parishId,
            query.Page,
            query.Size,
            query.Sort,
            cancellationToken);

        return new GetSeasonsResult(
            seasons.Select(SeasonDto.From).ToList(),
            totalItems);
    }
}
