using FluentAssertions;
using ParishCoordination.Contracts.Events;
using Resource.Application.Scope;
using Resource.Domain.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.UnitTests.Scope;

public sealed class ScopeReplicationTests
{
    [Fact]
    public async Task Handler_bootstraps_parents_and_replay_does_not_reapply()
    {
        var scopes = new FakeScopeStore();
        var tasks = new FakeTaskProjectionStore();
        var inbox = new MemoryInbox();
        var handler = new ScopeReplicationHandler(inbox, new(scopes), new(scopes),
            new(tasks, scopes, new(new FakePurchaseRequestStore())));
        var source = TaskSnapshot();

        await handler.ApplyAsync(source, default);
        await handler.ApplyAsync(source, default);

        scopes.Seasons.Should().ContainSingle().Which.SeasonId.Should().Be(source.Season.SeasonId);
        scopes.Scopes.Should().ContainSingle().Which.SeasonStatus.Should().Be(SeasonStatus.Active);
        tasks.Tasks.Should().ContainSingle().Which.TaskId.Should().Be(source.TaskId);
        inbox.Applied.Should().Be(1);
        var archived = source with
        {
            EventId = Guid.NewGuid(), SourceVersion = 5,
            Season = source.Season with { Status = "ARCHIVED", SourceVersion = 4 }
        };
        await handler.ApplyAsync(archived, default);
        await handler.ApplyAsync(source with { EventId = Guid.NewGuid() }, default);
        scopes.Scopes.Single().SeasonStatus.Should().Be(SeasonStatus.Archived);
    }

    [Fact]
    public void Area_snapshot_keeps_authorization_fields_without_overwriting_parent_status()
    {
        var season = Season();
        var area = Area(season);
        var mapped = ScopeReplicationMapping.Area(area, season);
        mapped.LeaderUserId.Should().Be(area.LeaderUserId);
        mapped.MaterialOfficerUserId.Should().Be(area.MaterialOfficerUserId);
        mapped.CommunityId.Should().Be(area.CommunityId);
        mapped.SeasonStatus.Should().BeNull();
        mapped.Status.Should().Be(WorkAreaStatus.ReadinessReview);
        mapped.Version.Should().Be(7);
    }

    [Fact]
    public void Task_snapshot_can_bootstrap_missing_projection_and_remove_previous_assignees()
    {
        var source = TaskSnapshot();
        var task = TaskProjection.FromSnapshot(ScopeReplicationMapping.Task(source), DateTimeOffset.UtcNow);
        task.AssigneeUserIds.Should().Equal(source.AssigneeUserIds);
        var newer = source with { EventId = Guid.NewGuid(), SourceVersion = 5, AssigneeUserIds = [], Status = "COMPLETED" };
        task.Apply(ScopeReplicationMapping.Task(newer), DateTimeOffset.UtcNow).Should().BeTrue();
        task.AssigneeUserIds.Should().BeEmpty();
        task.Status.Should().Be(ProjectedTaskStatus.Completed);
        task.Apply(ScopeReplicationMapping.Task(source), DateTimeOffset.UtcNow).Should().BeFalse();
        task.Status.Should().Be(ProjectedTaskStatus.Completed);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    public void Unknown_or_numeric_status_is_rejected(string value)
    {
        Action parse = () => ScopeReplicationMapping.Status<SeasonStatus>(value);
        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void Cross_parish_parent_is_rejected()
    {
        var season = Season();
        Action map = () => ScopeReplicationMapping.Area(Area(season) with { ParishId = Guid.NewGuid() }, season);
        map.Should().Throw<FormatException>();
    }

    private static SeasonScopeSnapshot Season() => new(Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 1), new(2027, 1, 15), "ACTIVE", 3);
    private static WorkAreaScopeSnapshot Area(SeasonScopeSnapshot season) => new(Guid.NewGuid(), season.SeasonId,
        season.ParishId, "Area", Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), null, null, "READINESS_REVIEW", 7, null, null);
    private static TaskScopeSnapshot TaskSnapshot()
    {
        var season = Season();
        var area = Area(season);
        return new(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, Guid.NewGuid(), area.WorkAreaId, "Task", "WORK", "IN_PROGRESS",
            null, [Guid.NewGuid()], 4,
            new(season.SeasonId, season.ParishId, season.StartDate, season.EndDate, season.Status, season.Version),
            new(area.WorkAreaId, area.SeasonId, area.ParishId, area.Name, area.CommunityId, area.LeaderUserId,
                area.MaterialOfficerUserId, null, null, area.Status, area.Version));
    }

    private sealed class MemoryInbox : IScopeReplicationInbox
    {
        private readonly HashSet<(string, Guid)> _seen = [];
        public int Applied { get; private set; }
        public async Task ExecuteAsync(string consumer, Guid eventId, Guid seasonId, Func<Task> apply, CancellationToken cancellationToken)
        {
            if (_seen.Contains((consumer, eventId))) return;
            await apply();
            _seen.Add((consumer, eventId));
            Applied++;
        }
    }
}
