namespace Reporting.Domain.Scope;

/// <summary>Task type as projected from TaskExecution (API Contract v1.3.2, Task.taskType).</summary>
public enum ProjectedTaskType
{
    Work,
    Purchase,
    Corrective
}

/// <summary>Task lifecycle as projected from TaskExecution (Contract v1.3.2: created in OPEN, no DRAFT).</summary>
public enum ProjectedTaskStatus
{
    Open,
    NotStarted,
    InProgress,
    Submitted,
    RevisionRequired,
    Completed,
    Cancelled
}

/// <summary>
/// <c>task_projection</c> (spec §2.2): the part of a TaskExecution task Readiness needs (CORRECTIVE tasks) — owning work area,
/// type, status and the volunteers holding an ACCEPTED assignment (material report submitter, PURCHASE buyer).
/// Fed by <c>task.*</c> events once TaskExecution publishes them; until then by the internal snapshot endpoint.
/// Older snapshots (<see cref="SourceVersion"/>) never overwrite newer ones.
/// </summary>
public sealed class TaskProjection
{
    private TaskProjection()
    {
    }

    public Guid TaskId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ProjectedTaskType Type { get; private set; }

    public ProjectedTaskStatus Status { get; private set; }

    public Guid? PurchaseRequestId { get; private set; }

    public Guid[] AssigneeUserIds { get; private set; } = [];

    public long SourceVersion { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsClosed => Status is ProjectedTaskStatus.Completed or ProjectedTaskStatus.Cancelled;

    public static TaskProjection FromSnapshot(TaskSnapshot snapshot, DateTimeOffset now)
    {
        var task = new TaskProjection { TaskId = snapshot.TaskId };
        task.Copy(snapshot, now);
        return task;
    }

    /// <summary>Returns false (and changes nothing) when the snapshot is older than what is stored.</summary>
    public bool Apply(TaskSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.TaskId != TaskId)
        {
            throw new ArgumentException("Snapshot belongs to another task.", nameof(snapshot));
        }

        if (snapshot.Version < SourceVersion)
        {
            return false;
        }

        Copy(snapshot, now);
        return true;
    }

    private void Copy(TaskSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.WorkAreaId == Guid.Empty)
        {
            throw DomainRuleException.Validation("TASK_PROJECTION_INVALID", "A task belongs to a work area.");
        }

        if ((snapshot.Type == ProjectedTaskType.Purchase) != (snapshot.PurchaseRequestId is not null))
        {
            throw DomainRuleException.Validation("TASK_PROJECTION_INVALID", "Only PURCHASE tasks carry a purchase request (and they must).");
        }

        WorkAreaId = snapshot.WorkAreaId;
        Name = (snapshot.Name ?? string.Empty).Trim();
        Type = snapshot.Type;
        Status = snapshot.Status;
        PurchaseRequestId = snapshot.PurchaseRequestId;
        AssigneeUserIds = snapshot.AssigneeUserIds.Distinct().ToArray();
        SourceVersion = snapshot.Version;
        UpdatedAt = now.ToUniversalTime();
    }
}

/// <summary>What <c>task.status.changed</c> / <c>task.assignment.responded</c> / <c>task.purchase-task.assigned</c> must carry.</summary>
public sealed record TaskSnapshot(
    Guid TaskId,
    Guid WorkAreaId,
    string? Name,
    ProjectedTaskType Type,
    ProjectedTaskStatus Status,
    Guid? PurchaseRequestId,
    IReadOnlyList<Guid> AssigneeUserIds,
    long Version);
