using System.Text.Json;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Infrastructure.TaskScopeSync;

public sealed class TaskScopeOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ScopeType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public long SourceVersion { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? LastError { get; set; }

    public static TaskScopeOutbox ForSeason(Season season) => new()
    {
        ScopeType = "season", AggregateId = season.Id, SourceVersion = season.Version,
        Payload = JsonSerializer.Serialize(SeasonSnapshot.From(season), ScopeJson.Options)
    };

    public static TaskScopeOutbox ForWorkArea(WorkArea area, Season season) => new()
    {
        ScopeType = "work-area", AggregateId = area.Id, SourceVersion = area.Version,
        Payload = JsonSerializer.Serialize(new WorkAreaDelivery(
            SeasonSnapshot.From(season), WorkAreaSnapshot.From(area, season)), ScopeJson.Options)
    };
}
