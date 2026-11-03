using FluentAssertions;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Events;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class TaskSnapshotTests
{
    [Fact]
    public void Snapshot_contains_parents_and_only_effective_assignees()
    {
        var (task, area, season) = Fixture();
        var now = DateTimeOffset.UtcNow;
        var accepted = TaskAssignment.Assign(task.Id, Guid.NewGuid(), Guid.NewGuid(), null, false, false, null, now);
        accepted.Accept(null, now);
        var pending = TaskAssignment.Apply(task.Id, Guid.NewGuid(), null, now);
        var cancelled = TaskAssignment.Assign(task.Id, Guid.NewGuid(), Guid.NewGuid(), null, false, false, null, now);
        cancelled.Accept(null, now);
        cancelled.CancelForTask("Cancelled");

        var snapshot = TaskSnapshotMapper.Map(task, area, season, [accepted, pending, cancelled], now);

        snapshot.AssigneeUserIds.Should().Equal(accepted.VolunteerId);
        snapshot.Season.ParishId.Should().Be(season.ParishId);
        snapshot.WorkArea.LeaderUserId.Should().Be(area.LeaderUserId);
        snapshot.TaskType.Should().Be("WORK");
        snapshot.Status.Should().Be("OPEN");
        snapshot.SourceVersion.Should().Be(task.Version);
        var row = TaskEventOutboxMessage.Create(snapshot);
        row.IsScopeSnapshot.Should().BeTrue();
        row.ToScopeSnapshot().AssigneeUserIds.Should().Equal(accepted.VolunteerId);
        row.ToScopeSnapshot().WorkArea.Should().Be(snapshot.WorkArea);
    }

    [Fact]
    public void Identity_is_stable_for_reconciliation_but_changes_with_source_version()
    {
        var (task, area, season) = Fixture();
        var first = TaskSnapshotMapper.Map(task, area, season, [], DateTimeOffset.UtcNow);
        var replay = TaskSnapshotMapper.Map(task, area, season, [], DateTimeOffset.UtcNow.AddMinutes(1));
        replay.EventId.Should().Be(first.EventId);
        task.MarkReady(null, DateTimeOffset.UtcNow);
        var next = TaskSnapshotMapper.Map(task, area, season, [], DateTimeOffset.UtcNow);
        next.EventId.Should().NotBe(first.EventId);
        next.Status.Should().Be("NOT_STARTED");
        next.SourceVersion.Should().Be(first.SourceVersion + 1);
    }

    [Fact]
    public void Mismatched_parent_ownership_cannot_be_published()
    {
        var (task, area, _) = Fixture();
        var (_, _, wrongSeason) = Fixture();
        Action map = () => TaskSnapshotMapper.Map(task, area, wrongSeason, [], DateTimeOffset.UtcNow);
        map.Should().Throw<InvalidOperationException>();
    }

    private static (WorkTask Task, WorkAreaScope Area, SeasonScope Season) Fixture()
    {
        var now = DateTimeOffset.UtcNow;
        var season = SeasonScope.Create(Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 1),
            new(2027, 1, 15), SeasonScopeStatus.Active, 3, now);
        var area = WorkAreaScope.Create(Guid.NewGuid(), season.SeasonId, season.ParishId,
            "Area", null, Guid.NewGuid(), Guid.NewGuid(), null, null, WorkAreaScopeStatus.Planned, 2, now);
        var task = WorkTask.CreateOpen(Guid.NewGuid(), area.WorkAreaId, area.LeaderUserId!.Value,
            "Task", null, null, TaskPriority.Medium, 60, 2, now, now.AddDays(1), now);
        return (task, area, season);
    }
}
