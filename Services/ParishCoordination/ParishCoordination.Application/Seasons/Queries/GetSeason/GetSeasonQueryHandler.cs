using BuildingBlocks.Security;

namespace ParishCoordination.Application.Seasons.Queries.GetSeason;

public sealed class GetSeasonQueryHandler(
    ISeasonRepository seasonRepository,
    ICurrentUser currentUser)
{
    public async Task<SeasonDto?> HandleAsync(
        GetSeasonQuery query,
        CancellationToken cancellationToken = default)
    {
        var season = await seasonRepository.GetByIdAsync(
            query.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return null;
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);
        return SeasonDto.From(season);
    }
}