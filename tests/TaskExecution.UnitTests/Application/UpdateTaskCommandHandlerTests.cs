using BuildingBlocks.Security;
using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class UpdateTaskCommandHandlerTests
{
    [Fact]
    public async Task Partial_update_preserves_omitted_fields_and_increments_version()
    {
        var fixture = CreateFixture();
        var originalDeadline = fixture.Task.Deadline;
        var command = EmptyCommand(fixture.Task.Id) with
        {
            Name = "  Updated task  ",
            Description = "   ",
            ExpectedVersion = 1
        };

        var result = await fixture.Handler.HandleAsync(command);

        result.Name.Should().Be("Updated task");
        result.Description.Should().BeNull();
        result.Deadline.Should().Be(originalDeadline);
        result.Version.Should().Be(2);
        fixture.Repository.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Stale_version_returns_precondition_failure()
    {
        var fixture = CreateFixture();
        var command = EmptyCommand(fixture.Task.Id) with { Name = "Updated", ExpectedVersion = 9 };

        var act = () => fixture.Handler.HandleAsync(command);

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
            EmptyCommand(fixture.Task.Id) with { Name = "Updated" });

        var error = (await act.Should().ThrowAsync<DomainRuleException>()).Which;
        error.Code.Should().Be("TASK_VERSION_MISMATCH");
        error.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Combined_old_and_new_schedule_is_validated_against_work_area()
    {
        var fixture = CreateFixture();
        var command = EmptyCommand(fixture.Task.Id) with
        {
            Deadline = new DateTimeOffset(2026, 12, 25, 12, 0, 0, TimeSpan.Zero)
        };

        var act = () => fixture.Handler.HandleAsync(command);

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code
            .Should().Be("TASK_DATE_OUT_OF_WORK_AREA");
    }

    [Fact]
    public async Task Leader_not_assigned_to_work_area_is_denied()
    {
        var fixture = CreateFixture(assignedLeaderId: Guid.NewGuid());

        var act = () => fixture.Handler.HandleAsync(
            EmptyCommand(fixture.Task.Id) with { Name = "Updated" });

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code
            .Should().Be("WORK_AREA_ACCESS_DENIED");
    }

    [Fact]
    public async Task Missing_task_returns_task_not_found()
    {
        var fixture = CreateFixture();

        var act = () => fixture.Handler.HandleAsync(
            EmptyCommand(Guid.NewGuid()) with { Name = "Updated" });

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_NOT_FOUND");
    }

    [Fact]
    public async Task Archived_season_blocks_update()
    {
        var fixture = CreateFixture(seasonStatus: SeasonScopeStatus.Archived);

        var act = () => fixture.Handler.HandleAsync(
            EmptyCommand(fixture.Task.Id) with { Name = "Updated" });

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SEASON_READ_ONLY");
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
            Guid.NewGuid(), workArea.WorkAreaId, userId, "Original", "Description", "Criteria",
            TaskPriority.Medium, 120, 2,
            new DateTimeOffset(2026, 12, 10, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 18, 17, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow);
        var currentUser = new FakeCurrentUser(userId, parishId);
        var repository = new FakeTaskRepository(task);
        var access = new TaskScopeAccess(new FakeScopeStore(season, workArea), currentUser);
        return new Fixture(new UpdateTaskCommandHandler(repository, access), repository, task);
    }

    private static UpdateTaskCommand EmptyCommand(Guid taskId) =>
        new(taskId, null, null, null, null, null, null, null, null, null);

    private sealed record Fixture(
        UpdateTaskCommandHandler Handler,
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
            Guid workAreaId, TaskState? status, TaskType? taskType, int page, int size, string? sort,
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
