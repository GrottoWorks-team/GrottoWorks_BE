using BuildingBlocks.Security;
using Readiness.Application.Abstractions;
using Readiness.Application.Checklists;
using Readiness.Application.Scope;
using Readiness.Domain.Checklists;
using Readiness.Domain.Scope;

namespace Readiness.UnitTests;

public sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    public sealed record Published(string EventType, int Version, object Payload, string AggregateType, Guid AggregateId);

    public List<Published> Events { get; } = [];

    public Task PublishAsync<TPayload>(string eventType, int version, TPayload payload, string aggregateType, Guid aggregateId, CancellationToken cancellationToken = default)
        where TPayload : class
    {
        Events.Add(new Published(eventType, version, payload, aggregateType, aggregateId));
        return Task.CompletedTask;
    }
}

public sealed class FakeCurrentUser(Guid userId, string role, Guid? parishId = null) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId { get; } = userId;

    public string Role { get; } = role;

    public Guid? ParishId { get; } = parishId;

    public Guid? CommunityId => null;
}

public sealed class FakeScopeStore : IWorkAreaScopeStore
{
    public List<WorkAreaScope> Scopes { get; } = [];

    public Task<WorkAreaScope?> GetAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Scopes.FirstOrDefault(s => s.WorkAreaId == workAreaId));

    public Task<IReadOnlyList<WorkAreaScope>> ListBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkAreaScope>>(Scopes.Where(s => s.SeasonId == seasonId).ToList());

    public Task<IReadOnlyList<WorkAreaScope>> ListByIdsAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkAreaScope>>(Scopes.Where(s => workAreaIds.Contains(s.WorkAreaId)).ToList());

    public Task<IReadOnlyList<WorkAreaScope>> ListByParishAsync(Guid parishId, Guid? seasonId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkAreaScope>>(
            Scopes.Where(s => s.ParishId == parishId && (seasonId is null || s.SeasonId == seasonId)).OrderBy(s => s.Name).ToList());

    public void Add(WorkAreaScope scope) => Scopes.Add(scope);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Adds a work area (IN_PROGRESS by default) of an ACTIVE season led by <paramref name="leaderId"/>.</summary>
    public WorkAreaScope Seed(
        Guid parishId,
        Guid leaderId,
        Guid? seasonId = null,
        string name = "Hang đá",
        SeasonStatus seasonStatus = SeasonStatus.Active,
        WorkAreaStatus status = WorkAreaStatus.InProgress)
    {
        var scope = WorkAreaScope.FromSnapshot(
            new WorkAreaSnapshot(Guid.NewGuid(), seasonId ?? Guid.NewGuid(), parishId, name, status, seasonStatus, null, leaderId, null, 1),
            DateTimeOffset.UtcNow);
        Scopes.Add(scope);
        return scope;
    }
}

public sealed class FakeTaskProjectionStore : ITaskProjectionStore
{
    public List<TaskProjection> Tasks { get; } = [];

    public Task<TaskProjection?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tasks.FirstOrDefault(t => t.TaskId == taskId));

    public void Add(TaskProjection task) => Tasks.Add(task);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public TaskProjection Seed(
        Guid workAreaId,
        ProjectedTaskType type = ProjectedTaskType.Corrective,
        ProjectedTaskStatus status = ProjectedTaskStatus.InProgress,
        params Guid[] assignees)
    {
        var task = TaskProjection.FromSnapshot(
            new TaskSnapshot(Guid.NewGuid(), workAreaId, "Sửa dây điện", type, status, null, assignees, 1),
            DateTimeOffset.UtcNow);
        Tasks.Add(task);
        return task;
    }
}

public sealed class FakeReadinessCheckStore : IReadinessCheckStore
{
    public List<ReadinessChecklist> Checklists { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task<ReadinessChecklist?> GetAsync(Guid checkId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Checklists.FirstOrDefault(c => c.ReadinessChecklistId == checkId));

    public Task<bool> ExistsForWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Checklists.Any(c => c.WorkAreaId == workAreaId));

    public Task<IReadOnlyList<ReadinessChecklist>> ListByWorkAreasAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReadinessChecklist>>(Checklists.Where(c => workAreaIds.Contains(c.WorkAreaId)).ToList());

    public Task<IReadOnlyList<ReadinessChecklist>> ListByCorrectiveTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReadinessChecklist>>(Checklists.Where(c => c.Items.Any(i => i.CorrectiveTaskId == taskId)).ToList());

    public void Add(ReadinessChecklist checklist) => Checklists.Add(checklist);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}
