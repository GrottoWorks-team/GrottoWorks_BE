using BuildingBlocks.Security;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Application.WorkAreas;

namespace ParishCoordination.Application.WorkAreas.Queries.GetWorkArea;

public sealed class GetWorkAreaQueryHandler(
    IWorkAreaRepository workAreaRepository,
    ISeasonRepository seasonRepository,
    ICurrentUser currentUser)
{
    public async Task<WorkAreaDto?> HandleAsync(
        GetWorkAreaQuery query,
        CancellationToken cancellationToken = default)
    {
        var workArea = await workAreaRepository.GetByIdAsync(
            query.WorkAreaId,
            cancellationToken);

        if (workArea is null)
        {
            return null;
        }

        var season = await seasonRepository.GetByIdAsync(
            workArea.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return null;
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);
        return WorkAreaDto.From(workArea);
    }
}
