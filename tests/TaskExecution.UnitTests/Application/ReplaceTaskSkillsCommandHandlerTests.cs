using BuildingBlocks.Security;
using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class ReplaceTaskSkillsCommandHandlerTests
{
    [Fact]
    public async Task Leader_replaces_skills_and_increments_task_version()
    {
        var fixture = CreateFixture();
        var skillId = Guid.NewGuid();

        var result = await fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(
                fixture.Task.Id,
                [new TaskSkillRequirement(skillId, "  ADVANCED  ", 2)],
                1));

        result.Version.Should().Be(2);
        fixture.Task.Skills.Should().ContainSingle();
        fixture.Task.Skills.Single().SkillId.Should().Be(skillId);
        fixture.Task.Skills.Single().RequiredSkillLevel.Should().Be("ADVANCED");
        fixture.Repository.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Empty_list_removes_all_skills()
    {
        var fixture = CreateFixture();
        await fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(
                fixture.Task.Id,
                [new TaskSkillRequirement(Guid.NewGuid(), null, null)],
                null));

        var result = await fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(fixture.Task.Id, [], null));

        result.Version.Should().Be(3);
        fixture.Task.Skills.Should().BeEmpty();
        fixture.Repository.SaveCalls.Should().Be(2);
    }

    [Fact]
    public async Task Same_normalized_set_in_different_order_is_no_op()
    {
        var fixture = CreateFixture();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(
                fixture.Task.Id,
                [
                    new TaskSkillRequirement(first, "BASIC", 1),
                    new TaskSkillRequirement(second, null, 2)
                ],
                null));

        var result = await fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(
                fixture.Task.Id,
                [
                    new TaskSkillRequirement(second, "   ", 2),
                    new TaskSkillRequirement(first, "  BASIC  ", 1)
                ],
                2));

        result.Version.Should().Be(2);
        fixture.Repository.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_skill_is_rejected()
    {
        var fixture = CreateFixture();
        var skillId = Guid.NewGuid();

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(
                fixture.Task.Id,
                [
                    new TaskSkillRequirement(skillId, null, null),
                    new TaskSkillRequirement(skillId, "ADVANCED", 1)
                ],
                null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code
            .Should().Be("TASK_SKILL_DUPLICATE");
    }

    [Fact]
    public async Task Unassigned_leader_is_denied()
    {
        var fixture = CreateFixture(assignedLeaderId: Guid.NewGuid());

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(fixture.Task.Id, [], null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code
            .Should().Be("WORK_AREA_ACCESS_DENIED");
    }

    [Fact]
    public async Task Archived_season_blocks_replacement()
    {
        var fixture = CreateFixture(seasonStatus: SeasonScopeStatus.Archived);

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(fixture.Task.Id, [], null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SEASON_READ_ONLY");
    }

    [Fact]
    public async Task Stale_version_returns_precondition_failure()
    {
        var fixture = CreateFixture();

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(
                fixture.Task.Id,
                [new TaskSkillRequirement(Guid.NewGuid(), null, null)],
                9));

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
            new ReplaceTaskSkillsCommand(
                fixture.Task.Id,
                [new TaskSkillRequirement(Guid.NewGuid(), null, null)],
                null));

        var error = (await act.Should().ThrowAsync<DomainRuleException>()).Which;
        error.Code.Should().Be("TASK_VERSION_MISMATCH");
        error.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Missing_task_returns_not_found()
    {
        var fixture = CreateFixture();

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(Guid.NewGuid(), [], null));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_NOT_FOUND");
    }

    [Fact]
    public async Task Non_editable_task_status_is_rejected()
    {
        var fixture = CreateFixture();
        typeof(WorkTask).GetProperty(nameof(WorkTask.Status))!.SetValue(fixture.Task, TaskState.InProgress);

        var act = () => fixture.Handler.HandleAsync(
            new ReplaceTaskSkillsCommand(fixture.Task.Id, [], null));

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
            Guid.NewGuid(), workArea.WorkAreaId, userId, "Task", null, null,
            TaskPriority.Medium, 60, 1, null, null, DateTimeOffset.UtcNow);
        var repository = new FakeTaskRepository(task);
        var access = new TaskScopeAccess(
            new FakeScopeStore(season, workArea),
            new FakeCurrentUser(userId, parishId));
        return new Fixture(new ReplaceTaskSkillsCommandHandler(repository, access), repository, task);
    }

    private sealed record Fixture(
        ReplaceTaskSkillsCommandHandler Handler,
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
