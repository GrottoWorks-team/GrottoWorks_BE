using FluentAssertions;
using ParishCoordination.Contracts.Events;
using Readiness.Application.Scope;
using Readiness.Domain.Scope;
using Readiness.Domain.Checklists;
using TaskExecution.Contracts.Events;

namespace Readiness.UnitTests.Scope;

public sealed class ScopeReplicationTests
{

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_version_season_and_area_snapshots_leave_all_fields_and_timestamps_unchanged(bool changed)
    {
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var parent = Season();
        var season = SeasonScope.Create(parent.SeasonId, SeasonStatus.Active, parent.Version, now);
        season.Apply(changed ? SeasonStatus.Archived : SeasonStatus.Active, parent.Version, now.AddHours(1)).Should().BeFalse();
        season.Status.Should().Be(SeasonStatus.Active);
        season.UpdatedAt.Should().Be(now);
        season.SourceVersion.Should().Be(parent.Version);

        var original = ScopeReplicationMapping.Area(Area(parent), parent) with { SeasonStatus = SeasonStatus.Active };
        var area = WorkAreaScope.FromSnapshot(original, now);
        var incoming = changed ? original with
        {
            Name = "Changed", Status = WorkAreaStatus.Completed, SeasonStatus = SeasonStatus.Archived,
            LeaderUserId = Guid.NewGuid(), MaterialOfficerUserId = Guid.NewGuid(), CommunityId = Guid.NewGuid()
        } : original;
        area.Apply(incoming, now.AddHours(1)).Should().BeFalse();
        area.Should().BeEquivalentTo(WorkAreaScope.FromSnapshot(original, now));
    }

    [Fact]
    public void Identical_season_status_fanout_does_not_touch_child_timestamp()
    {
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var parent = Season();
        var area = WorkAreaScope.FromSnapshot(ScopeReplicationMapping.Area(Area(parent), parent) with
            { SeasonStatus = SeasonStatus.Active }, now);
        area.ApplySeasonStatus(SeasonStatus.Active, now.AddHours(1));
        area.UpdatedAt.Should().Be(now);
        area.ApplySeasonStatus(SeasonStatus.Archived, now.AddHours(2));
        area.UpdatedAt.Should().Be(now.AddHours(2));
        area.ApplySeasonStatus(SeasonStatus.Archived, now.AddHours(3));
        area.UpdatedAt.Should().Be(now.AddHours(2));
    }

    [Fact]
    public async Task Newer_area_snapshot_uses_canonical_parent_even_when_explicit_embedded_status_is_stale()
    {
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var parent = Season();
        var original = ScopeReplicationMapping.Area(Area(parent), parent) with { SeasonStatus = SeasonStatus.Active };
        var scopes = new FakeScopeStore();
        scopes.AddSeason(SeasonScope.Create(parent.SeasonId, SeasonStatus.Archived, parent.Version + 1, now));
        scopes.Add(WorkAreaScope.FromSnapshot(original, now));
        var handler = new ApplyWorkAreaSnapshotHandler(scopes);

        var result = await handler.HandleAsync(original with { Version = original.Version + 1, Name = "New name" });

        result.Applied.Should().BeTrue();
        result.Scope.Name.Should().Be("New name");
        result.Scope.SeasonStatus.Should().Be(SeasonStatus.Archived);
        scopes.Seasons.Single().UpdatedAt.Should().Be(now);
        var created = await handler.HandleAsync(original with { WorkAreaId = Guid.NewGuid() });
        created.Scope.SeasonStatus.Should().Be(SeasonStatus.Archived);
    }

    [Fact]
    public async Task Independent_parent_advancement_fans_out_despite_equal_area_version_and_replays_preserve_timestamps()
    {
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var parent = Season();
        var sourceArea = Area(parent);
        var original = ScopeReplicationMapping.Area(sourceArea, parent) with { SeasonStatus = SeasonStatus.Active };
        var scopes = new FakeScopeStore();
        scopes.AddSeason(SeasonScope.Create(parent.SeasonId, SeasonStatus.Active, parent.Version, now));
        scopes.Add(WorkAreaScope.FromSnapshot(original, now));
        var handler = CreateReplicationHandler(scopes);
        PlanningScopeSnapshot Snapshot(SeasonScopeSnapshot season, WorkAreaScopeSnapshot area) =>
            new(Guid.NewGuid(), "work-area", area.WorkAreaId, area.Version, now.AddHours(1), season, area);

        // A new broker identity must still respect each aggregate's source version.
        await handler.ApplyAsync(Snapshot(parent, sourceArea), default);
        scopes.Seasons.Single().UpdatedAt.Should().Be(now);
        scopes.Scopes.Single().UpdatedAt.Should().Be(now);
        await handler.ApplyAsync(Snapshot(parent with { Status = "ARCHIVED" },
            sourceArea with { Name = "Conflicting area", LeaderUserId = Guid.NewGuid() }), default);
        scopes.Seasons.Single().Status.Should().Be(SeasonStatus.Active);
        scopes.Seasons.Single().UpdatedAt.Should().Be(now);
        scopes.Scopes.Single().Should().BeEquivalentTo(WorkAreaScope.FromSnapshot(original, now));

        var advanced = parent with { Status = "ARCHIVED", Version = parent.Version + 1 };
        await handler.ApplyAsync(Snapshot(advanced, sourceArea), default);
        var area = scopes.Scopes.Single();
        area.SeasonStatus.Should().Be(SeasonStatus.Archived);
        area.SourceVersion.Should().Be(original.Version);
        area.Name.Should().Be(original.Name);
        var updatedAt = area.UpdatedAt;
        var parentUpdatedAt = scopes.Seasons.Single().UpdatedAt;
        updatedAt.Should().BeAfter(now);

        await handler.ApplyAsync(Snapshot(advanced, sourceArea), default);
        await handler.ApplyAsync(Snapshot(parent, sourceArea), default);
        area.UpdatedAt.Should().Be(updatedAt);
        scopes.Seasons.Single().UpdatedAt.Should().Be(parentUpdatedAt);
        area.SeasonStatus.Should().Be(SeasonStatus.Archived);

        // A newer parent version with the same status advances only the parent timestamp/version.
        await handler.ApplyAsync(Snapshot(advanced with { Version = advanced.Version + 1 }, sourceArea), default);
        area.UpdatedAt.Should().Be(updatedAt);
        scopes.Seasons.Single().SourceVersion.Should().Be(advanced.Version + 1);
    }

    private static ScopeReplicationHandler CreateReplicationHandler(FakeScopeStore scopes) =>
        new(new MemoryInbox(), new(scopes), new(scopes), new(new FakeTaskProjectionStore(), scopes,
            new(new FakeReadinessCheckStore())));

    [Fact]
    public async Task Completed_corrective_snapshot_moves_linked_item_to_recheck()
    {
        var source = TaskSnapshot() with { TaskType = "CORRECTIVE", Status = "COMPLETED" };
        var checklist = ReadinessChecklist.Create(source.WorkAreaId, "Inspection", [new("Safety", null, true)]);
        var item = checklist.Items.Single();
        checklist.RecordResult(item.ReadinessItemId, InspectionResult.Failed, "Needs correction", Guid.NewGuid(), DateTimeOffset.UtcNow);
        checklist.LinkCorrectiveTask(item.ReadinessItemId, source.TaskId, false);
        var checks = new FakeReadinessCheckStore();
        checks.Add(checklist);
        var scopes = new FakeScopeStore();
        var handler = new ScopeReplicationHandler(new MemoryInbox(), new(scopes), new(scopes),
            new(new FakeTaskProjectionStore(), scopes, new(checks)));

        await handler.ApplyAsync(source, default);

        item.Status.Should().Be(ItemStatus.Recheck);
        item.CorrectiveTaskId.Should().Be(source.TaskId);
    }

    [Fact]
    public async Task Handler_bootstraps_parents_and_replay_does_not_reapply()
    {
        var scopes = new FakeScopeStore();
        var tasks = new FakeTaskProjectionStore();
        var inbox = new MemoryInbox();
        var handler = new ScopeReplicationHandler(inbox, new(scopes), new(scopes),
            new(tasks, scopes, new(new FakeReadinessCheckStore())));
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
