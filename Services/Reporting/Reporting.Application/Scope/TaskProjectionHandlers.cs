using Reporting.Domain;
using Reporting.Domain.Scope;

namespace Reporting.Application.Scope;

public interface ITaskProjectionStore
{
    /// <summary>Tracked.</summary>
    Task<TaskProjection?> GetAsync(Guid taskId, CancellationToken cancellationToken = default);

    void Add(TaskProjection task);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record TaskProjectionDto(
    Guid TaskId,
    Guid WorkAreaId,
    string Name,
    ProjectedTaskType Type,
    ProjectedTaskStatus Status,
    Guid? PurchaseRequestId,
    IReadOnlyList<Guid> AssigneeUserIds,
    long Version)
{
    public static TaskProjectionDto From(TaskProjection task) =>
        new(task.TaskId, task.WorkAreaId, task.Name, task.Type, task.Status, task.PurchaseRequestId, task.AssigneeUserIds, task.SourceVersion);
}

/// <summary>Applies a task snapshot (internal feed today, <c>task.*</c> consumers later) — idempotent by version.</summary>
public sealed class ApplyTaskSnapshotHandler(ITaskProjectionStore store, IWorkAreaScopeStore scopes)
{
    public async Task<(TaskProjectionDto Task, bool Applied)> HandleAsync(TaskSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (await scopes.GetAsync(snapshot.WorkAreaId, cancellationToken) is null)
        {
            throw DomainRuleException.Validation("WORK_AREA_NOT_PROJECTED", "Project the work area before its tasks.");
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await store.GetAsync(snapshot.TaskId, cancellationToken);
        if (existing is null)
        {
            var created = TaskProjection.FromSnapshot(snapshot, now);
            store.Add(created);
            await store.SaveChangesAsync(cancellationToken);
            return (TaskProjectionDto.From(created), true);
        }

        var applied = existing.Apply(snapshot, now);
        if (applied)
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return (TaskProjectionDto.From(existing), applied);
    }
}
