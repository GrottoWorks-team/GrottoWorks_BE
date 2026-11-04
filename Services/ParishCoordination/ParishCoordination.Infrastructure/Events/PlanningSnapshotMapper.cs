using System.Text.Json;
using ParishCoordination.Contracts.Events;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Infrastructure.Events;

public static class PlanningSnapshotMapper
{
    public static JsonElement SeasonPayload(SeasonScopeSnapshot season, IEnumerable<WorkArea> areas)
    {
        var payload = JsonSerializer.SerializeToNode(season, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        var recipients = areas.Where(area => area.SeasonId == season.SeasonId)
            .SelectMany(area => new[] { area.LeaderUserId, area.MaterialOfficerUserId })
            .Where(id => id.HasValue && id.Value != Guid.Empty).Select(id => id!.Value).Distinct().Order().ToArray();
        payload["recipientUserIds"] = JsonSerializer.SerializeToNode(recipients);
        return JsonSerializer.SerializeToElement(payload);
    }

    public static JsonElement AssignmentPayload(WorkAreaScopeSnapshot area, bool communityChanged,
        bool leaderChanged, bool materialOfficerChanged)
    {
        // Keep all existing snapshot fields; the additive flags describe this transaction, not current ownership.
        var payload = JsonSerializer.SerializeToNode(area, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        payload["communityChanged"] = communityChanged;
        payload["leaderChanged"] = leaderChanged;
        payload["materialOfficerChanged"] = materialOfficerChanged;
        return JsonSerializer.SerializeToElement(payload);
    }

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
