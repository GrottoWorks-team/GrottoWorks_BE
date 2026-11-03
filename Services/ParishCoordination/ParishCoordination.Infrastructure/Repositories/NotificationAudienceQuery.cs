using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Repositories;

public sealed class NotificationAudienceQuery(ParishCoordinationDbContext db) : INotificationAudienceQuery
{
    public async Task<IReadOnlyList<WorkAreaAudience>?> GetAsync(Guid parishId, Guid seasonId, CancellationToken ct)
    {
        // One statement gives a complete, consistent audience even while planning backfill is pending.
        var result = await db.Seasons.AsNoTracking()
            .Where(season => season.Id == seasonId && season.ParishId == parishId)
            .Select(season => db.WorkAreas.Where(area => area.SeasonId == season.Id)
                .OrderBy(area => area.Id)
                .Select(area => new WorkAreaAudience(area.Id, season.Id, season.ParishId,
                    area.LeaderUserId, area.MaterialOfficerUserId)).ToList())
            .SingleOrDefaultAsync(ct);
        return result;
    }
}
