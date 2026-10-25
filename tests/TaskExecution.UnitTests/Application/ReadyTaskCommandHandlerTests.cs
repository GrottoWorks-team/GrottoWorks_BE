using BuildingBlocks.Security;
using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class ReadyTaskCommandHandlerTests
{
    [Fact]
    public async Task Assigned_leader_marks_open_task_ready()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(
            new ReadyTaskCommand(fixture.Task.Id, 1));

        result.Status.Should().Be(TaskState.NotStarted);
        result.Version.Should().Be(2);
        fixture.Repository.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Missing_task_returns_not_found()
    {
        var fixture = CreateFixture();

        var act = () => fixture.Handler.HandleAsync(new ReadyTaskCommand(Guid.NewGuid(), null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_NOT_FOUND");
    }

    [Fact]
    public async Task Leader_not_assigned_to_work_area_is_denied()
    {
        var fixture = CreateFixture(assignedLeaderId: Guid.NewGuid());

        var act = () => fixture.Handler.HandleAsync(new ReadyTaskCommand(fixture.Task.Id, null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code
            .Should().Be("WORK_AREA_ACCESS_DENIED");
    }

    [Fact]
    public async Task Archived_season_blocks_transition()
    {
        var fixture = CreateFixture(seasonStatus: SeasonScopeStatus.Archived);

        var act = () => fixture.Handler.HandleAsync(new ReadyTaskCommand(fixture.Task.Id, null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SEASON_READ_ONLY");
    }

    [Fact]
    public async Task Stale_version_returns_precondition_failure()
    {
        var fixture = CreateFixture();

        var act = () => fixture.Handler.HandleAsync(new ReadyTaskCommand(fixture.Task.Id, 9));

        var error = (await act.Should().ThrowAsync<DomainRuleException>()).Which;
        error.Code.Should().Be("TASK_VERSION_MISMATCH");
        error.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Database_concurrency_race_returns_precondition_failure()
    {
        var fixture = CreateFixture();
        fixture.Repository.ThrowConcurrency = true;

        var act = () => fixture.Handler.HandleAsync(new ReadyTaskCommand(fixture.Task.Id, null));

        var error = (await act.Should().ThrowAsync<DomainRuleException>()).Which;
        error.Code.Should().Be("TASK_VERSION_MISMATCH");
        error.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }

    private static Fixture CreateFixture(
        Guid? assignedLeaderId = null,
        SeasonScopeStatus seasonStatus = SeasonScopeStatus.Active)
    {
        var userId = Guid.NewGuid();
        var parishId = Guid.NewGuid();
        var season = SeasonScope.Create(
            Guid.NewGuid(), parishId, new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15),
            seasonStatus, 1, DateTimeOffset.UtcNow);
        var workArea = WorkAreaScope.Create(
            Guid.NewGuid(), season.SeasonId, parishId, "Area", null, assignedLeaderId ?? userId, null,
            new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 20),
            WorkAreaScopeStatus.InProgress, 1, DateTimeOffset.UtcNow);
        var task = WorkTask.CreateOpen(
            Guid.NewGuid(), workArea.WorkAreaId, userId, "Task", null, null,
            TaskPriority.Medium, 60, 1, null,
            new DateTimeOffset(2026, 12, 10, 17, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow);
        var repository = new FakeTaskRepository(task);
        var scopeAccess = new TaskScopeAccess(
            new FakeScopeStore(season, workArea),
            new FakeCurrentUser(userId, parishId));

        return new Fixture(new ReadyTaskCommandHandler(repository, scopeAccess), repository, task);
    }

    private sealed record Fixture(
        ReadyTaskCommandHandler Handler,
        FakeTaskRepository Repository,
        WorkTask Task);

    private sealed class FakeCurrentUser(Guid userId, Guid parishId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid UserId => userId;
        public string Role => GrottoWorksRoles.Leader;
        public Guid? ParishId => parishId;
        public Guid? CommunityId => null;
    }

    private sealed class FakeScopeStore(SeasonScope season, WorkAreaScope workArea) : IScopeProjectionStore
    {
        public Task<SeasonScope?> GetSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
            Task.FromResult(season.SeasonId == seasonId ? season : null);

        public Task<WorkAreaScope?> GetWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(workArea.WorkAreaId == workAreaId ? workArea : null);

        public void Add(SeasonScope scope) => throw new NotSupportedException();
        public void Add(WorkAreaScope scope) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTaskRepository(WorkTask task) : ITaskRepository
    {
        public bool ThrowConcurrency { get; set; }
        public int SaveCalls { get; private set; }

        public Task<WorkTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(task.Id == taskId ? task : null);

        public Task<WorkTask?> GetByIdForUpdateAsync(
            Guid taskId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(task.Id == taskId ? task : null);

        public Task<(IReadOnlyList<WorkTask> Items, long TotalItems)> GetPagedByWorkAreaAsync(
            Guid workAreaId,
            TaskState? status,
            TaskType? taskType,
            int page,
            int size,
            string? sort,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<WorkTask>, long)>(([], 0));

        public void Add(WorkTask newTask) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (ThrowConcurrency)
            {
                throw new TaskConcurrencyException(new InvalidOperationException("race"));
            }

            return Task.CompletedTask;
        }
    }
}
