using FluentAssertions;
using ParishCoordination.Contracts.Events;
using ParishCoordination.Infrastructure.Events;
using Xunit;

namespace ParishCoordination.UnitTests;

public sealed class PlanningSnapshotReplayTests
{
    [Fact]
    public void Replay_keeps_snapshot_identity_version_and_payload()
    {
        var season = new SeasonScopeSnapshot(Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 1), new(2027, 1, 15), "ACTIVE", 3);
        var snapshot = new PlanningScopeSnapshot(Guid.NewGuid(), "season", season.SeasonId, season.Version, DateTimeOffset.UtcNow, season, null);
        var row = PlanningEventOutboxMessage.Create(snapshot);
        var envelope = row.Envelope;
        row.MarkFailed(DateTimeOffset.UtcNow);
        row.MarkPublished(DateTimeOffset.UtcNow);
        row.ScheduleSnapshotReplay(DateTimeOffset.UtcNow).Should().BeTrue();
        row.Id.Should().Be(snapshot.SnapshotId);
        row.SourceVersion.Should().Be(3);
        row.Envelope.Should().Be(envelope);
        row.Attempts.Should().Be(0);
        row.PublishedAt.Should().BeNull();
    }

    [Fact]
    public void Reconciliation_preserves_pending_backoff()
    {
        var season = new SeasonScopeSnapshot(Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 1), new(2027, 1, 15), "ARCHIVED", 5);
        var row = PlanningEventOutboxMessage.Create(new PlanningScopeSnapshot(Guid.NewGuid(), "season", season.SeasonId, 5, DateTimeOffset.UtcNow, season, null));
        var now = DateTimeOffset.UtcNow;
        row.MarkFailed(now);
        row.ScheduleSnapshotReplay(now.AddMinutes(1)).Should().BeFalse();
        row.NextAttemptAt.Should().Be(now.AddSeconds(2));
        row.Attempts.Should().Be(1);
    }
}
