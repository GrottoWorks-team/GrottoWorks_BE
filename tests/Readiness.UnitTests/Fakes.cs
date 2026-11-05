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

    public Action? BeforePublish { get; set; }

    public List<Published> Events { get; } = [];

    public Task PublishAsync<TPayload>(string eventType, int version, TPayload payload, string aggregateType, Guid aggregateId, CancellationToken cancellationToken = default)
        where TPayload : class
    {
        BeforePublish?.Invoke();
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

    public List<SeasonScope> Seasons { get; } = [];

    public Task<SeasonScope?> GetSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Seasons.FirstOrDefault(season => season.SeasonId == seasonId));

    public void AddSeason(SeasonScope season) => Seasons.Add(season);

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

/// <summary>Accepts every file unless <see cref="Reject"/> is set (then throws it, like the NotificationAudit check).</summary>
public sealed class FakeFileVerifier : Readiness.Application.Files.IFileVerifier
{
    public async Task<IAsyncDisposable> ReserveAsync(IReadOnlyCollection<Guid> ids, string purpose, Guid userId, Guid referenceId, CancellationToken ct = default)
    {
        await EnsureAttachableAsync(ids, purpose, userId, ct);
        return new TestReservation();
    }
    private sealed class TestReservation : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }

    public List<(IReadOnlyCollection<Guid> Ids, string Purpose, Guid Uploader)> Calls { get; } = [];

    public Readiness.Domain.DomainRuleException? Reject { get; set; }

    public Task EnsureAttachableAsync(IReadOnlyCollection<Guid> fileIds, string purpose, Guid uploaderUserId, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileIds, purpose, uploaderUserId));
        return Reject is null || fileIds.Count == 0 ? Task.CompletedTask : Task.FromException(Reject);
    }
}


public sealed class FakeCorrectiveTaskCreator : Readiness.Application.Checklists.ICorrectiveTaskCreator
{
    public List<Readiness.Application.Checklists.CorrectiveTaskCreationRequest> Requests { get; } = [];
    public Guid TaskId { get; } = Guid.NewGuid();
    public Task<Readiness.Application.Checklists.CreatedCorrectiveTask> CreateAsync(Readiness.Application.Checklists.CorrectiveTaskCreationRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(new Readiness.Application.Checklists.CreatedCorrectiveTask(TaskId, request.WorkAreaId, "CORRECTIVE", "OPEN"));
    }
}

/// <summary>Runs the operation inline and records whether a caller is inside the unit of work (outbox atomicity checks).</summary>
public sealed class FakeUnitOfWork : Readiness.Application.Abstractions.IUnitOfWork
{
    public int Depth { get; private set; }

    public bool IsActive => Depth > 0;

    public int Committed { get; private set; }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        Depth++;
        try
        {
            var result = await operation();
            if (Depth == 1)
            {
                Committed++;
            }

            return result;
        }
        finally
        {
            Depth--;
        }
    }
}
