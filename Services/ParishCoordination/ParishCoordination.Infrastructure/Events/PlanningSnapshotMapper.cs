using System.Text.Json;
using ParishCoordination.Contracts.Events;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Infrastructure.Events;

public static class PlanningSnapshotMapper
{
    public static SeasonScopeSnapshot Season(Season season) => new(season.Id, season.ParishId,
        season.StartDate, season.EndDate, Status(season.Status), season.Version);

    public static WorkAreaScopeSnapshot WorkArea(WorkArea area, Season season) => new(area.Id,
        area.SeasonId, season.ParishId, area.Name, area.CommunityId, area.WorkAreaCategoryId,
        area.LeaderUserId, area.MaterialOfficerUserId, area.StartDate, area.Deadline,
        Status(area.Status), area.Version, area.Latitude, area.Longitude);

    public static PlanningScopeSnapshot Scope(Season season, WorkArea? area = null) => new(
        Guid.NewGuid(), area is null ? "season" : "work-area", area?.Id ?? season.Id,
        area?.Version ?? season.Version, DateTimeOffset.UtcNow, Season(season),
        area is null ? null : WorkArea(area, season));

    private static string Status<T>(T status) where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(status.ToString());
}
