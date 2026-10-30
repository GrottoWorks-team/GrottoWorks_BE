using System.Text.Json;
using System.Text.Json.Serialization;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Infrastructure.TaskScopeSync;

public sealed record SeasonSnapshot(
    Guid ParishId, DateOnly StartDate, DateOnly EndDate, string Status, long Version)
{
    public static SeasonSnapshot From(Season season) => new(
        season.ParishId, season.StartDate, season.EndDate, ScopeJson.Status(season.Status), season.Version);
}

public sealed record WorkAreaSnapshot(
    Guid SeasonId, Guid ParishId, string Name, Guid? CommunityId,
    Guid? LeaderUserId, Guid? MaterialOfficerUserId, DateOnly? StartDate,
    DateOnly? Deadline, string Status, long Version)
{
    public static WorkAreaSnapshot From(WorkArea area, Season season) => new(
        area.SeasonId, season.ParishId, area.Name, area.CommunityId, area.LeaderUserId,
        area.MaterialOfficerUserId, area.StartDate, area.Deadline, ScopeJson.Status(area.Status), area.Version);
}

public sealed record WorkAreaDelivery(SeasonSnapshot Season, WorkAreaSnapshot WorkArea);

public static class ScopeJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    public static string Status<T>(T status) where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(status.ToString());
}
