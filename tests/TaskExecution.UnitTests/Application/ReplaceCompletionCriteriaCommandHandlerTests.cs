using BuildingBlocks.Security;
using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class ReplaceCompletionCriteriaCommandHandlerTests
{
    [Fact]
    public async Task Leader_replaces_criteria_and_increments_version()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(
            new ReplaceCompletionCriteriaCommand(fixture.Task.Id, "  Lights work safely  ", 1));

        result.CompletionCriteria.Should().Be("Lights work safely");
        result.Version.Should().Be(2);
        fixture.Repository.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Blank_text_clears_criteria()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(
            new ReplaceCompletionCriteriaCommand(fixture.Task.Id, "   ", null));

        result.CompletionCriteria.Should().BeNull();
        fixture.Repository.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Normalized_equal_value_is_no_op()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(
            new ReplaceCompletionCriteriaCommand(fixture.Task.Id, "  Original criteria  ", 1));

        result.Version.Should().Be(1);
        fixture.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Stale_version_returns_precondition_failure_even_for_equal_value()
    {
        var fixture = CreateFixture();

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceCompletionCriteriaCommand(fixture.Task.Id, "Original criteria", 9));

        var error = (await act.Should().ThrowAsync<DomainRuleException>()).Which;
        error.Code.Should().Be("TASK_VERSION_MISMATCH");
        error.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Database_concurrency_race_returns_precondition_failure()
    {
        var fixture = CreateFixture();
        fixture.Repository.ThrowConcurrency = true;

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceCompletionCriteriaCommand(fixture.Task.Id, "Changed", null));

        var error = (await act.Should().ThrowAsync<DomainRuleException>()).Which;
        error.Code.Should().Be("TASK_VERSION_MISMATCH");
        error.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Missing_task_returns_not_found()
    {
        var fixture = CreateFixture();

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceCompletionCriteriaCommand(Guid.NewGuid(), "Changed", null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_NOT_FOUND");
    }

    [Fact]
    public async Task Unassigned_leader_is_denied()
    {
        var fixture = CreateFixture(assignedLeaderId: Guid.NewGuid());

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceCompletionCriteriaCommand(fixture.Task.Id, "Changed", null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code
            .Should().Be("WORK_AREA_ACCESS_DENIED");
    }

    [Fact]
    public async Task Archived_season_blocks_change()
    {
        var fixture = CreateFixture(seasonStatus: SeasonScopeStatus.Archived);

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceCompletionCriteriaCommand(fixture.Task.Id, "Changed", null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SEASON_READ_ONLY");
    }

    [Fact]
    public async Task Non_editable_task_status_is_rejected()
    {
        var fixture = CreateFixture();
        typeof(WorkTask).GetProperty(nameof(WorkTask.Status))!.SetValue(fixture.Task, TaskState.InProgress);

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceCompletionCriteriaCommand(fixture.Task.Id, "Changed", null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_INVALID_STATE");
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
        var task = WorkTask.CreateDraft(
            Guid.NewGuid(), workArea.WorkAreaId, userId, "Task", null, "Original criteria",
            TaskPriority.Medium, 60, 1, null, null, DateTimeOffset.UtcNow);
        var repository = new FakeTaskRepository(task);
        var access = new TaskScopeAccess(
            new FakeScopeStore(season, workArea),
            new FakeCurrentUser(userId, parishId));
        return new Fixture(new ReplaceCompletionCriteriaCommandHandler(repository, access), repository, task);
    }

    private sealed record Fixture(
        ReplaceCompletionCriteriaCommandHandler Handler,
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
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTaskRepository(WorkTask task) : ITaskRepository
    {
        public bool ThrowConcurrency { get; set; }
        public int SaveCalls { get; private set; }

        public Task<WorkTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(task.Id == taskId ? task : null);

        public Task<WorkTask?> GetByIdForUpdateAsync(Guid taskId, CancellationToken cancellationToken = default) =>
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
