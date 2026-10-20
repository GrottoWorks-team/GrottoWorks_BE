using BuildingBlocks.Security;
using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class CreateTaskCommandHandlerTests
{
    [Fact]
    public async Task Assigned_leader_creates_work_task_in_draft()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(ValidCommand(fixture.WorkArea.WorkAreaId));

        result.Status.Should().Be(TaskState.Draft);
        result.TaskType.Should().Be(TaskType.Work);
        result.RequiredVolunteers.Should().Be(2);
        fixture.Tasks.Items.Should().ContainSingle();
        fixture.Tasks.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Unassigned_leader_is_denied()
    {
        var fixture = CreateFixture(currentUserId: Guid.NewGuid(), assignedLeaderId: Guid.NewGuid());

        var act = () => fixture.Handler.HandleAsync(ValidCommand(fixture.WorkArea.WorkAreaId));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("WORK_AREA_ACCESS_DENIED");
    }

    [Fact]
    public async Task Archived_season_is_read_only()
    {
        var fixture = CreateFixture(seasonStatus: SeasonScopeStatus.Archived);

        var act = () => fixture.Handler.HandleAsync(ValidCommand(fixture.WorkArea.WorkAreaId));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SEASON_READ_ONLY");
    }

    [Fact]
    public async Task Schedule_outside_work_area_is_rejected()
    {
        var fixture = CreateFixture();
        var command = ValidCommand(fixture.WorkArea.WorkAreaId) with
        {
            Deadline = new DateTimeOffset(2026, 12, 25, 12, 0, 0, TimeSpan.Zero)
        };

        var act = () => fixture.Handler.HandleAsync(command);

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_DATE_OUT_OF_WORK_AREA");
    }

    private static Fixture CreateFixture(
        Guid? currentUserId = null,
        Guid? assignedLeaderId = null,
        SeasonScopeStatus seasonStatus = SeasonScopeStatus.Active)
    {
        var parishId = Guid.NewGuid();
        var userId = currentUserId ?? assignedLeaderId ?? Guid.NewGuid();
        var leaderId = assignedLeaderId ?? userId;
        var season = SeasonScope.Create(
            Guid.NewGuid(), parishId, new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15),
            seasonStatus, 1, DateTimeOffset.UtcNow);
        var workArea = WorkAreaScope.Create(
            Guid.NewGuid(), season.SeasonId, parishId, "Area", null, leaderId, null,
            new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 20),
            WorkAreaScopeStatus.InProgress, 1, DateTimeOffset.UtcNow);
        var scopes = new FakeScopeStore(season, workArea);
        var currentUser = new FakeCurrentUser(userId, parishId);
        var tasks = new FakeTaskRepository();
        var access = new TaskScopeAccess(scopes, currentUser);
        return new Fixture(new CreateTaskCommandHandler(tasks, access, currentUser), tasks, workArea);
    }

    private static CreateTaskCommand ValidCommand(Guid workAreaId) =>
        new(
            workAreaId, "Install lights", "Description", "All lights operate safely",
            TaskPriority.High, 360, 2,
            new DateTimeOffset(2026, 12, 10, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 18, 12, 0, 0, TimeSpan.Zero));

    private sealed record Fixture(
        CreateTaskCommandHandler Handler,
        FakeTaskRepository Tasks,
        WorkAreaScope WorkArea);

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

    private sealed class FakeTaskRepository : ITaskRepository
    {
        public List<WorkTask> Items { get; } = [];
        public int SaveCalls { get; private set; }
        public Task<WorkTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkTask?>(null);
        public Task<WorkTask?> GetByIdForUpdateAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkTask?>(null);
        public Task<(IReadOnlyList<WorkTask> Items, long TotalItems)> GetPagedByWorkAreaAsync(
            Guid workAreaId, TaskState? status, TaskType? taskType, int page, int size, string? sort,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<WorkTask>, long)>(([], 0));
        public void Add(WorkTask task) => Items.Add(task);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }
}
