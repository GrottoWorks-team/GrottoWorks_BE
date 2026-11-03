using System.Text.Json;
using FluentAssertions;
using TaskExecution.Contracts.Events;
using TaskExecution.Infrastructure.Events;
using Xunit;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class TaskSnapshotReplayTests
{
    [Fact]
    public void Replay_preserves_envelope_and_identity_and_only_resets_delivery_metadata()
    {
        var row = Snapshot();
        var envelope = row.Envelope;
        var id = row.Id;
        var now = DateTimeOffset.UtcNow;
        row.MarkFailed(now);
        row.MarkPublished(now.AddSeconds(3));
        row.ScheduleSnapshotReplay(now.AddMinutes(1)).Should().BeTrue();
        row.Id.Should().Be(id);
        row.Envelope.Should().Be(envelope);
        row.PublishedAt.Should().BeNull();
        row.Attempts.Should().Be(0);
        row.LastError.Should().BeNull();
        row.NextAttemptAt.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void Pending_retry_backoff_is_not_overridden_by_reconciliation()
    {
        var row = Snapshot();
        var now = DateTimeOffset.UtcNow;
        row.MarkFailed(now);
        row.ScheduleSnapshotReplay(now.AddMinutes(1)).Should().BeFalse();
        row.NextAttemptAt.Should().Be(now.AddSeconds(2));
        row.Attempts.Should().Be(1);
    }

    [Fact]
    public void Reconciliation_cannot_replay_a_business_event()
    {
        var row = TaskEventOutboxMessage.Create(new TaskIntegrationEvent(Guid.NewGuid(), TaskEventTypes.StatusChanged,
            1, DateTimeOffset.UtcNow, "test", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "task", Guid.NewGuid(), 2,
            JsonSerializer.SerializeToElement(new { status = "OPEN" })));
        row.MarkPublished(DateTimeOffset.UtcNow);
        Action replay = () => row.ScheduleSnapshotReplay(DateTimeOffset.UtcNow);
        replay.Should().Throw<InvalidOperationException>();
    }

    private static TaskEventOutboxMessage Snapshot()
    {
        var season = new TaskSeasonSnapshot(Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 1), new(2027, 1, 15), "ACTIVE", 1);
        var area = new TaskWorkAreaSnapshot(Guid.NewGuid(), season.SeasonId, season.ParishId, "Area", null, null, null, null, null, "PLANNED", 1);
        return TaskEventOutboxMessage.Create(new TaskScopeSnapshot(Guid.NewGuid(), 1, DateTimeOffset.UtcNow,
            Guid.NewGuid(), area.WorkAreaId, "Task", "WORK", "OPEN", null, [], 1, season, area, []));
    }
}
