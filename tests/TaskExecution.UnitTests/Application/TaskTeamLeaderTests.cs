using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Identity.Contracts.Events;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class TaskTeamLeaderTests
{
    [Fact]
    public async Task Select_replace_and_clear_only_change_the_task_version_and_designation()
    {
        var f = new Fixture();
        var first = f.AddAssignment(f.Source.VolunteerId);
        var second = f.AddAssignment(f.AddVolunteer());
        var initial = f.Task.Version;
        var set = await f.Service.SetAsync(f.Task.Id, first.Id, initial, default);
        set.TeamLeaderAssignmentId.Should().Be(first.Id);
        set.Version.Should().Be(initial + 1);

        var same = await f.Service.SetAsync(f.Task.Id, first.Id, set.Version, default);
        same.Version.Should().Be(set.Version);
        f.Source.Store.Saves.Should().Be(1);
        var stale = () => f.Service.SetAsync(f.Task.Id, first.Id, initial, default);
        (await stale.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_VERSION_MISMATCH");

        var replaced = await f.Service.SetAsync(f.Task.Id, second.Id, same.Version, default);
        replaced.TeamLeaderAssignmentId.Should().Be(second.Id);
        var cleared = await f.Service.ClearAsync(f.Task.Id, replaced.Version, default);
        cleared.TeamLeaderAssignmentId.Should().BeNull();
        var repeated = await f.Service.ClearAsync(f.Task.Id, cleared.Version, default);
        repeated.Version.Should().Be(initial + 3);
        f.Source.Store.Saves.Should().Be(3);
        f.Task.Status.Should().Be(TaskState.Open);
        first.Status.Should().Be(AssignmentStatus.Accepted);
        second.Status.Should().Be(AssignmentStatus.Accepted);
        f.Source.Store.FreshReadInsideTransaction.Should().BeTrue();
    }

    [Theory]
    [InlineData("ADMIN", false)]
    [InlineData("LEADER", true)]
    public async Task No_op_clear_still_checks_work_area_ownership(string role, bool anotherLeader)
    {
        var f = new Fixture();
        f.Source.User.Role = role;
        if (anotherLeader) f.Source.User.UserId = Guid.NewGuid();
        var action = () => f.Service.ClearAsync(f.Task.Id, f.Task.Version, default);
        (await action.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("WORK_AREA_ACCESS_DENIED");
        f.Source.Store.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData("ACTIVE", "ADVANCED", true)]
    [InlineData("ACTIVE", "EXPERT", true)]
    [InlineData("ACTIVE", "BASIC", false)]
    [InlineData("ACTIVE", null, false)]
    [InlineData("LOCKED", "EXPERT", false)]
    public async Task Requires_one_matching_skill_at_level_even_for_an_overridden_assignment(
        string status, string? level, bool eligible)
    {
        var f = new Fixture();
        var skill = Guid.NewGuid();
        f.Task.ReplaceSkills([new(skill, "ADVANCED", null), new(Guid.NewGuid(), null, null)], null, DateTimeOffset.UtcNow);
        var volunteer = f.Source.Identity.Items[f.Source.VolunteerId];
        f.Source.Identity.Items[volunteer.UserId] = volunteer with
        {
            Status = status,
            SkillIds = [skill],
            Skills = [new(skill, level)]
        };
        var assignment = f.AddAssignment(volunteer.UserId, overrideSkill: true);
        var action = () => f.Service.SetAsync(f.Task.Id, assignment.Id, f.Task.Version, default);
        if (eligible)
            (await action()).TeamLeaderAssignmentId.Should().Be(assignment.Id);
        else
            (await action.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_TEAM_LEADER_NOT_ELIGIBLE");
    }

    [Fact]
    public async Task Rejects_pending_cancelled_and_foreign_assignments()
    {
        var f = new Fixture();
        var pending = f.AddAssignment(f.Source.VolunteerId, accepted: false);
        var cancelled = f.AddAssignment(f.Source.VolunteerId);
        cancelled.CancelForTask("Cancelled");
        var foreign = f.AddAssignment(f.Source.VolunteerId, taskId: Guid.NewGuid());
        foreach (var candidate in new[] { pending, cancelled, foreign })
        {
            var action = () => f.Service.SetAsync(f.Task.Id, candidate.Id, f.Task.Version, default);
            (await action.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should()
                .Be(candidate == foreign ? "TASK_TEAM_LEADER_ASSIGNMENT_MISMATCH" : "TASK_TEAM_LEADER_NOT_ELIGIBLE");
        }
    }

    [Fact]
    public async Task Skill_replacement_clears_the_designation_in_its_single_version_change()
    {
        var f = new Fixture();
        var assignment = f.AddAssignment(f.Source.VolunteerId);
        await f.Service.SetAsync(f.Task.Id, assignment.Id, f.Task.Version, default);
        var version = f.Task.Version;
        var result = await f.ReplaceSkills.HandleAsync(new(f.Task.Id, [new(Guid.NewGuid(), "BASIC", null)], version));
        result.TeamLeaderAssignmentId.Should().BeNull();
        result.Version.Should().Be(version + 1);
        f.Tasks.Saves.Should().Be(1);
        assignment.Status.Should().Be(AssignmentStatus.Accepted);
    }

    [Fact]
    public async Task Cancellation_clears_the_designation_without_an_extra_version_change()
    {
        var f = new Fixture();
        var assignment = f.AddAssignment(f.Source.VolunteerId);
        await f.Service.SetAsync(f.Task.Id, assignment.Id, f.Task.Version, default);
        var version = f.Task.Version;
        var result = await f.Source.Service.CancelTaskAsync(f.Task.Id, "Cancelled", version);
        result.TeamLeaderAssignmentId.Should().BeNull();
        result.Version.Should().Be(version + 1);
        assignment.Status.Should().Be(AssignmentStatus.Cancelled);
    }

    [Fact]
    public async Task Identity_inbox_handles_same_status_version_stale_delivery_and_retry()
    {
        var f = new Fixture();
        var assignment = f.AddAssignment(f.Source.VolunteerId);
        var skill = Guid.NewGuid();
        var volunteer = f.Source.Identity.Items[f.Source.VolunteerId] with
        {
            SkillIds = [skill], Skills = [new(skill, "ADVANCED")]
        };
        f.Source.Identity.Items[volunteer.UserId] = volunteer;
        f.Task.ReplaceSkills([new(skill, "ADVANCED", null)], null, DateTimeOffset.UtcNow);
        await f.Service.SetAsync(f.Task.Id, assignment.Id, f.Task.Version, default);
        var version = f.Task.Version;
        var first = f.Event();
        var second = f.Event();
        await f.Ingestion.HandleAsync(first, default);
        f.Task.Version.Should().Be(version);

        f.Source.Identity.Items[volunteer.UserId] = volunteer with { Skills = [new(skill, "BASIC")] };
        f.Lookup.Unavailable = true;
        var failed = () => f.Ingestion.HandleAsync(second, default);
        (await failed.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("IDENTITY_SERVICE_UNAVAILABLE");
        f.Inbox.Processed.Should().NotContain(second.EventId);
        f.Task.Version.Should().Be(version);
        f.Lookup.Unavailable = false;
        await f.Ingestion.HandleAsync(second, default);
        f.Task.TeamLeaderAssignmentId.Should().BeNull();
        f.Task.Version.Should().Be(version + 1);

        f.Source.Identity.Items[volunteer.UserId] = volunteer;
        await f.Service.SetAsync(f.Task.Id, assignment.Id, f.Task.Version, default);
        await f.Ingestion.HandleAsync(first, default);
        await f.Ingestion.HandleAsync(second with { EventId = Guid.NewGuid(), OccurredAt = first.OccurredAt.AddMinutes(-1) }, default);
        f.Task.TeamLeaderAssignmentId.Should().Be(assignment.Id);
        f.Task.Version.Should().Be(version + 2);
    }

    [Fact]
    public async Task Lock_and_unlock_preserve_status_projection_order_and_completed_history()
    {
        var f = new Fixture();
        var assignment = f.AddAssignment(f.Source.VolunteerId);
        await f.Service.SetAsync(f.Task.Id, assignment.Id, f.Task.Version, default);
        var volunteer = f.Source.Identity.Items[f.Source.VolunteerId];
        f.Source.Identity.Items[volunteer.UserId] = volunteer with { Status = "LOCKED" };
        await f.Ingestion.HandleAsync(f.Event(IdentityUserLocked.EventType, 2), default);
        f.StatusProjection.User!.Status.Should().Be("LOCKED");
        f.Task.TeamLeaderAssignmentId.Should().BeNull();
        assignment.Status.Should().Be(AssignmentStatus.Accepted);

        f.Source.Identity.Items[volunteer.UserId] = volunteer;
        await f.Ingestion.HandleAsync(f.Event(IdentityUserUnlocked.EventType, 3), default);
        f.Task.TeamLeaderAssignmentId.Should().BeNull();
        await f.Service.SetAsync(f.Task.Id, assignment.Id, f.Task.Version, default);
        var now = DateTimeOffset.UtcNow;
        f.Task.MarkReady(null, now);
        f.Task.BeginWork(now);
        f.Task.RecordProgress(true, false, null, now);
        f.Task.ReviewCompletion(true, null, now);
        assignment.CompleteForTask();
        f.Source.Identity.Items[volunteer.UserId] = volunteer with { Status = "LOCKED" };
        await f.Ingestion.HandleAsync(f.Event(IdentityUserLocked.EventType, 2), default);
        f.StatusProjection.User.Status.Should().Be("ACTIVE");
        await f.Ingestion.HandleAsync(f.Event(IdentityUserLocked.EventType, 4), default);
        f.StatusProjection.User.Status.Should().Be("LOCKED");
        foreach (var action in new Func<Task<TaskDto>>[]
        {
            () => f.Service.SetAsync(f.Task.Id, assignment.Id, f.Task.Version, default),
            () => f.Service.ClearAsync(f.Task.Id, f.Task.Version, default)
        })
            (await action.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_INVALID_STATE");
        f.Task.TeamLeaderAssignmentId.Should().Be(assignment.Id);
    }

    [Fact]
    public async Task One_volunteer_can_lead_more_than_one_task_and_is_removed_from_each_when_inactive()
    {
        var f = new Fixture();
        var volunteerId = f.Source.VolunteerId;
        var first = f.AddAssignment(volunteerId);
        var otherTask = WorkTask.CreateOpen(Guid.NewGuid(), f.Task.WorkAreaId, f.Source.User.UserId, "Other task",
            null, null, TaskPriority.Medium, 30, 1, null, f.Task.Deadline, DateTimeOffset.UtcNow);
        f.Tasks.Add(otherTask);
        var second = f.AddAssignment(volunteerId, taskId: otherTask.Id);
        await f.Service.SetAsync(f.Task.Id, first.Id, f.Task.Version, default);
        await f.Service.SetAsync(otherTask.Id, second.Id, otherTask.Version, default);
        f.Task.TeamLeaderAssignmentId.Should().Be(first.Id);
        otherTask.TeamLeaderAssignmentId.Should().Be(second.Id);

        f.Source.Identity.Items[volunteerId] = f.Source.Identity.Items[volunteerId] with { Status = "INACTIVE" };
        await f.Ingestion.HandleAsync(f.Event(), default);
        f.Task.TeamLeaderAssignmentId.Should().BeNull();
        otherTask.TeamLeaderAssignmentId.Should().BeNull();
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("skills")]
    [InlineData("cancel")]
    public async Task Pending_put_serializes_with_other_task_mutations(string operation)
    {
        var f = new Fixture();
        var assignment = f.AddAssignment(f.Source.VolunteerId);
        var version = f.Task.Version;
        var reachedIdentity = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Lookup.BeforeFind = async userId =>
        {
            if (userId != assignment.VolunteerId || !reachedIdentity.TrySetResult()) return;
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        var setting = f.Service.SetAsync(f.Task.Id, assignment.Id, version, default);
        await reachedIdentity.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var modifying = operation switch
        {
            "delete" => f.Service.ClearAsync(f.Task.Id, version, default),
            "skills" => f.ReplaceSkills.HandleAsync(new(f.Task.Id, [new(Guid.NewGuid(), null, null)], null)),
            _ => f.Source.Service.CancelTaskAsync(f.Task.Id, "Cancelled", null)
        };
        try { modifying.IsCompleted.Should().BeFalse(); }
        finally { resume.TrySetResult(); }
        (await setting.WaitAsync(TimeSpan.FromSeconds(5))).TeamLeaderAssignmentId.Should().Be(assignment.Id);
        if (operation == "delete")
        {
            var error = await Assert.ThrowsAsync<DomainRuleException>(() => modifying.WaitAsync(TimeSpan.FromSeconds(5)));
            error.Code.Should().Be("TASK_VERSION_MISMATCH");
            f.Task.TeamLeaderAssignmentId.Should().Be(assignment.Id);
        }
        else
        {
            var result = await modifying.WaitAsync(TimeSpan.FromSeconds(5));
            result.TeamLeaderAssignmentId.Should().BeNull();
            result.Version.Should().Be(version + 2);
        }
    }

    [Fact]
    public async Task Consumer_rechecks_the_designation_after_a_concurrent_replacement()
    {
        var f = new Fixture();
        var first = f.AddAssignment(f.Source.VolunteerId);
        var second = f.AddAssignment(f.AddVolunteer());
        await f.Service.SetAsync(f.Task.Id, first.Id, f.Task.Version, default);
        var reachedIdentity = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Lookup.BeforeFind = async userId =>
        {
            if (userId != first.VolunteerId) return;
            reachedIdentity.SetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        f.Source.Identity.Items[first.VolunteerId] = f.Source.Identity.Items[first.VolunteerId] with { Status = "INACTIVE" };
        var consuming = f.Ingestion.HandleAsync(f.Event(), default);
        await reachedIdentity.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await f.Service.SetAsync(f.Task.Id, second.Id, f.Task.Version, default).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { resume.TrySetResult(); }
        await consuming.WaitAsync(TimeSpan.FromSeconds(5));
        f.Task.TeamLeaderAssignmentId.Should().Be(second.Id);
        f.Source.Store.Saves.Should().Be(2);
    }

    internal sealed class Fixture : ITaskTeamLeaderQuery
    {
        public AssignmentTestFixture Source { get; } = new();
        public WorkTask Task => Source.Task;
        public TaskTeamLeaderService Service { get; }
        public ReplaceTaskSkillsCommandHandler ReplaceSkills { get; }
        public IdentityStatusIngestionHandler Ingestion { get; }
        public AssignmentTestFixture.TestTasks Tasks { get; }
        public Lookup Lookup { get; }
        public TestInbox Inbox { get; } = new();
        public StatusStore StatusProjection { get; } = new();

        public Fixture()
        {
            var scopes = new AssignmentTestFixture.TestScopes(Source.Season, Source.Area);
            var access = new TaskScopeAccess(scopes, Source.User);
            var transaction = new UserLock();
            Tasks = new(Task);
            Lookup = new(Source.Identity);
            Service = new(Tasks, Source.Store, this, transaction, scopes, access,
                new ActiveUserAccess(Lookup, Source.User), Lookup);
            ReplaceSkills = new(Tasks, access, Source.Store, Lookup);
            Ingestion = new(Inbox, transaction, StatusProjection, Service);
        }

        public Guid AddVolunteer()
        {
            var id = Guid.NewGuid();
            Source.Identity.Items[id] = Source.Identity.Items[Source.VolunteerId] with { UserId = id };
            return id;
        }

        public TaskAssignment AddAssignment(Guid volunteerId, bool accepted = true, Guid? taskId = null, bool overrideSkill = false)
        {
            var row = TaskAssignment.Assign(taskId ?? Task.Id, volunteerId, Source.User.UserId, null,
                false, overrideSkill, overrideSkill ? "Earlier override" : null, DateTimeOffset.UtcNow);
            if (accepted) row.Accept(null, DateTimeOffset.UtcNow);
            Source.Store.Add(row);
            return row;
        }

        public IdentityIntegrationEvent Event(string type = IdentityUserEligibilityChanged.EventType, long version = 1) =>
            new(Guid.NewGuid(), type, 1, DateTimeOffset.UtcNow, "team-leader-test", null, Source.User.ParishId,
                null, "user", Source.VolunteerId, version, JsonSerializer.SerializeToElement(new { userId = Source.VolunteerId }));

        public Task<IReadOnlyList<Guid>> FindActiveTaskIdsAsync(Guid volunteerId, CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<Guid>>(Tasks.Items
                .Where(task => task.Status is not (TaskState.Completed or TaskState.Cancelled) &&
                    Source.Store.Items.Any(row => row.Id == task.TeamLeaderAssignmentId && row.VolunteerId == volunteerId))
                .Select(task => task.Id).ToList());
    }

    internal sealed class Lookup(AssignmentTestFixture.TestUsers users) : IIdentityUserLookup
    {
        public bool Unavailable { get; set; }
        public Func<Guid, Task>? BeforeFind { get; set; }
        public async Task<IdentityUser?> FindAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (Unavailable)
                throw new DomainRuleException("IDENTITY_SERVICE_UNAVAILABLE", "Identity offline.", DomainErrorKind.ServiceUnavailable);
            if (BeforeFind is not null)
                await BeforeFind(userId);
            return await users.FindAsync(userId, cancellationToken);
        }
    }

    internal sealed class TestInbox : IScopeEventInbox
    {
        public HashSet<Guid> Processed { get; } = [];
        public async Task ExecuteAsync(string consumer, Guid eventId, Func<Task> apply, CancellationToken cancellationToken)
        {
            if (Processed.Contains(eventId)) return;
            await apply();
            Processed.Add(eventId);
        }
    }

    internal sealed class StatusStore : IUserStatusProjectionStore
    {
        public UserStatusProjection? User { get; private set; }
        public Task<UserStatusProjection?> GetAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(User);
        public void Add(UserStatusProjection projection) => User = projection;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UserLock : IScopeSnapshotTransaction
    {
        private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gates = new();
        public async Task<T> ExecuteAsync<T>(string scopeType, Guid scopeId, Func<Task<T>> apply, CancellationToken cancellationToken)
        {
            var gate = gates.GetOrAdd(scopeId, _ => new SemaphoreSlim(1));
            await gate.WaitAsync(cancellationToken);
            try { return await apply(); }
            finally { gate.Release(); }
        }
    }
}
