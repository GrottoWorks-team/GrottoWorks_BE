using System.Security.Cryptography;
using System.Text;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Events;

public static class TaskSnapshotMapper
{
    public static TaskScopeSnapshot Map(WorkTask task, WorkAreaScope area, SeasonScope season,
        IEnumerable<TaskAssignment> assignments, DateTimeOffset capturedAt, IEnumerable<TaskMaterial>? materials = null)
    {
        if (task.WorkAreaId != area.WorkAreaId || area.SeasonId != season.SeasonId || area.ParishId != season.ParishId)
            throw new InvalidOperationException("Task snapshot ownership does not match its parent scopes.");

        // Stable across a retry or reconciliation of the same source version.
        // New identity revision permits a complete backfill without rewriting old immutable envelopes.
        var identity = SHA256.HashData(Encoding.UTF8.GetBytes($"task-scope:deadline-v2:{task.Id:D}:{task.Version}"));
        var recipients = assignments.Where(row => row.TaskId == task.Id &&
                row.Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress or AssignmentStatus.Completed)
            .Select(row => row.VolunteerId).Distinct().Order().ToArray();

        return new TaskScopeSnapshot(new Guid(identity.AsSpan(0, 16)), 1, capturedAt.ToUniversalTime(),
            task.Id, area.WorkAreaId, task.Name, EnumText.ToText(task.Type), EnumText.ToText(task.Status),
            task.PurchaseRequestId, recipients, task.Version,
            new TaskSeasonSnapshot(season.SeasonId, season.ParishId, season.StartDate, season.EndDate,
                EnumText.ToText(season.Status), season.SourceVersion),
            new TaskWorkAreaSnapshot(area.WorkAreaId, area.SeasonId, area.ParishId, area.Name,
                area.CommunityId, area.LeaderUserId, area.MaterialOfficerUserId, area.StartDate, area.Deadline,
                EnumText.ToText(area.Status), area.SourceVersion),
            (materials ?? task.Materials).Where(row => row.TaskId == task.Id).OrderBy(row => row.MaterialId)
                .Select(row => new TaskMaterialSnapshot(row.MaterialId, row.RequiredQuantity, row.Note)).ToArray(), task.Deadline);
    }
}
