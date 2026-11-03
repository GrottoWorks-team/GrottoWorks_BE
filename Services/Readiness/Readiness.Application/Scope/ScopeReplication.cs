using ParishCoordination.Contracts.Events;
using Readiness.Domain.Scope;
using TaskExecution.Contracts.Events;

namespace Readiness.Application.Scope;

public interface IScopeReplicationInbox
{
    Task ExecuteAsync(string consumer, Guid eventId, Guid seasonId, Func<Task> apply, CancellationToken cancellationToken);
}

public static class ScopeReplicationMapping
{
    public static T Status<T>(string value) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !char.IsAsciiLetter(character) && character != '_') ||
            !Enum.TryParse<T>(value.Replace("_", ""), true, out var result) || !Enum.IsDefined(result))
            throw new FormatException("Unknown scope status.");
        return result;
    }

    public static void Validate(Guid id, long version)
    {
        if (id == Guid.Empty || version < 1) throw new FormatException("Scope ID and positive source version are required.");
    }

    public static WorkAreaSnapshot Area(WorkAreaScopeSnapshot area, SeasonScopeSnapshot season)
    {
        Validate(area.WorkAreaId, area.Version);
        Validate(season.SeasonId, season.Version);
        if (season.EndDate <= season.StartDate) throw new FormatException("Season dates are invalid.");
        if (area.SeasonId != season.SeasonId || area.ParishId != season.ParishId || area.ParishId == Guid.Empty)
            throw new FormatException("Work area parent ownership is inconsistent.");
        return new(area.WorkAreaId, area.SeasonId, area.ParishId, area.Name,
            Status<WorkAreaStatus>(area.Status), null, area.CommunityId,
            area.LeaderUserId, area.MaterialOfficerUserId, area.Version);
    }

    public static TaskSnapshot Task(TaskScopeSnapshot snapshot)
    {
        Validate(snapshot.TaskId, snapshot.SourceVersion);
        if (snapshot.SchemaVersion != 1 || snapshot.EventId == Guid.Empty ||
            snapshot.WorkAreaId != snapshot.WorkArea.WorkAreaId || snapshot.AssigneeUserIds is null ||
            snapshot.AssigneeUserIds.Any(id => id == Guid.Empty))
            throw new FormatException("Invalid task snapshot envelope.");
        return new(snapshot.TaskId, snapshot.WorkAreaId, snapshot.Name,
            Status<ProjectedTaskType>(snapshot.TaskType), Status<ProjectedTaskStatus>(snapshot.Status),
            snapshot.PurchaseRequestId, snapshot.AssigneeUserIds, snapshot.SourceVersion);
    }
}

public sealed class ScopeReplicationHandler(IScopeReplicationInbox inbox,
    ApplySeasonStatusHandler seasons, ApplyWorkAreaSnapshotHandler areas, ApplyTaskSnapshotHandler tasks)
{
    public Task ApplyAsync(PlanningScopeSnapshot snapshot, CancellationToken cancellationToken)
    {
        ScopeReplicationMapping.Validate(snapshot.AggregateId, snapshot.SourceVersion);
        if (snapshot.SnapshotId == Guid.Empty || snapshot.Season is null ||
            snapshot.ScopeType is not ("season" or "work-area"))
            throw new FormatException("Invalid planning snapshot envelope.");
        ScopeReplicationMapping.Validate(snapshot.Season.SeasonId, snapshot.Season.Version);
        if (snapshot.Season.ParishId == Guid.Empty || snapshot.Season.EndDate <= snapshot.Season.StartDate ||
            (snapshot.ScopeType == "season" && snapshot.WorkArea is not null) ||
            (snapshot.ScopeType == "season" && (snapshot.AggregateId != snapshot.Season.SeasonId || snapshot.SourceVersion != snapshot.Season.Version)))
            throw new FormatException("Planning snapshot identity is inconsistent.");
        WorkAreaSnapshot? area = null;
        if (snapshot.ScopeType == "work-area")
        {
            if (snapshot.WorkArea is null || snapshot.WorkArea.WorkAreaId != snapshot.AggregateId || snapshot.WorkArea.Version != snapshot.SourceVersion)
                throw new FormatException("Work area snapshot identity is inconsistent.");
            area = ScopeReplicationMapping.Area(snapshot.WorkArea, snapshot.Season);
        }
        var status = ScopeReplicationMapping.Status<SeasonStatus>(snapshot.Season.Status);
        return inbox.ExecuteAsync("readiness-planning", snapshot.SnapshotId, snapshot.Season.SeasonId, async () =>
        {
            await seasons.HandleAsync(new(snapshot.Season.SeasonId, status, snapshot.Season.Version), cancellationToken);
            if (area is not null) await areas.HandleAsync(area, cancellationToken);
        }, cancellationToken);
    }

    public Task ApplyAsync(TaskScopeSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Season is null || snapshot.WorkArea is null) throw new FormatException("Task parent scopes are required.");
        var task = ScopeReplicationMapping.Task(snapshot);
        var season = new SeasonScopeSnapshot(snapshot.Season.SeasonId, snapshot.Season.ParishId,
            snapshot.Season.StartDate, snapshot.Season.EndDate, snapshot.Season.Status, snapshot.Season.SourceVersion);
        var sourceArea = snapshot.WorkArea;
        var area = ScopeReplicationMapping.Area(new(sourceArea.WorkAreaId, sourceArea.SeasonId, sourceArea.ParishId,
            sourceArea.Name, sourceArea.CommunityId, null, sourceArea.LeaderUserId, sourceArea.MaterialOfficerUserId,
            sourceArea.StartDate, sourceArea.Deadline, sourceArea.Status, sourceArea.SourceVersion, null, null), season);
        var status = ScopeReplicationMapping.Status<SeasonStatus>(season.Status);
        return inbox.ExecuteAsync("readiness-task-snapshots", snapshot.EventId, season.SeasonId, async () =>
        {
            await seasons.HandleAsync(new(season.SeasonId, status, season.Version), cancellationToken);
            await areas.HandleAsync(area, cancellationToken);
            await tasks.HandleAsync(task, cancellationToken);
        }, cancellationToken);
    }
}
