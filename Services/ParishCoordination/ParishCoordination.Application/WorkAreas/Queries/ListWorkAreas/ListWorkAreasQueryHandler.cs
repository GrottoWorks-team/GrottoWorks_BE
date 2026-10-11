using BuildingBlocks.Security;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Application.WorkAreas;

namespace ParishCoordination.Application.WorkAreas.Queries.ListWorkAreas;

public sealed class ListWorkAreasQueryHandler(
    ISeasonRepository seasonRepository,
    IWorkAreaRepository workAreaRepository,
    ICurrentUser currentUser)
{
    public async Task<ListWorkAreasResult?> HandleAsync(
        ListWorkAreasQuery query,
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

        var (workAreas, totalItems) = await workAreaRepository.GetPagedBySeasonIdAsync(
            query.SeasonId,
            query.Page,
            query.Size,
            query.Sort,
            cancellationToken);

        return new ListWorkAreasResult(
            workAreas.Select(WorkAreaDto.From).ToList(),
            totalItems);
    }
}
