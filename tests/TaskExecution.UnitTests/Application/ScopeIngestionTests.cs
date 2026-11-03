using FluentAssertions;
using ParishCoordination.Contracts.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain.Scopes;
using SeasonSnapshot = ParishCoordination.Contracts.Events.SeasonScopeSnapshot;

namespace TaskExecution.UnitTests.Application;

public sealed class ScopeIngestionTests
{
    [Fact]
    public async Task Ingestion_applies_parent_before_child_and_ignores_replayed_message()
    {
        var store = new ProjectionStore();
        var inbox = new Inbox();
        var transaction = new ScopeTransaction();
        var handler = new PlanningScopeIngestionHandler(inbox,
            new ApplySeasonScopeSnapshotHandler(store, transaction),
            new ApplyWorkAreaScopeSnapshotHandler(store, transaction));
        var season = new SeasonSnapshot(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 1), "ACTIVE", 2);
        var leader = Guid.NewGuid();
        var area = new ParishCoordination.Contracts.Events.WorkAreaScopeSnapshot(Guid.NewGuid(), season.SeasonId,
            season.ParishId, "Area", Guid.NewGuid(), null, leader, Guid.NewGuid(), null, null, "PLANNED", 1, null, null);
        var snapshot = new PlanningScopeSnapshot(Guid.NewGuid(), "work-area", area.WorkAreaId, 1, DateTimeOffset.UtcNow, season, area);
        await handler.HandleAsync(snapshot, default);
        await handler.HandleAsync(snapshot, default);
        store.Order.Should().Equal("season", "work-area");
        store.Area!.LeaderUserId.Should().Be(leader);
        store.Area.ParishId.Should().Be(season.ParishId);
        store.Saves.Should().Be(2);
    }

    [Fact]
    public void Snapshot_with_inconsistent_tenant_is_rejected_before_writing()
    {
        var season = new SeasonSnapshot(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 1), "ACTIVE", 2);
        var area = new ParishCoordination.Contracts.Events.WorkAreaScopeSnapshot(Guid.NewGuid(), season.SeasonId,
            Guid.NewGuid(), "Area", null, null, null, null, null, null, "PLANNED", 1, null, null);
        var snapshot = new PlanningScopeSnapshot(Guid.NewGuid(), "work-area", area.WorkAreaId, 1, DateTimeOffset.UtcNow, season, area);
        var action = () => PlanningScopeIngestionHandler.Validate(snapshot);
        action.Should().Throw<FormatException>();
    }

    [Fact]
    public void Matching_scope_payload_is_valid()
    {
        var season = new SeasonSnapshot(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 1), "ARCHIVED", 3);
        var snapshot = new PlanningScopeSnapshot(Guid.NewGuid(), "season", season.SeasonId, 3, DateTimeOffset.UtcNow, season, null);
        var action = () => PlanningScopeIngestionHandler.Validate(snapshot);
        action.Should().NotThrow();
    }

    [Fact]
    public void User_status_ignores_duplicate_and_old_versions()
    {
        var user = UserStatusProjection.Create(Guid.NewGuid(), Guid.NewGuid(), "LOCKED", 3);
        user.Apply(user.ParishId, "ACTIVE", 2).Should().BeFalse();
        user.Apply(user.ParishId, "ACTIVE", 3).Should().BeFalse();
        user.Status.Should().Be("LOCKED");
        user.Apply(user.ParishId, "ACTIVE", 4).Should().BeTrue();
        user.Status.Should().Be("ACTIVE");
    }

    private sealed class ScopeTransaction : IScopeSnapshotTransaction
    {
        public Task<T> ExecuteAsync<T>(string type, Guid id, Func<Task<T>> apply, CancellationToken cancellationToken = default) => apply();
    }

    private sealed class Inbox : IScopeEventInbox
    {
        private readonly HashSet<Guid> processed = [];
        public async Task ExecuteAsync(string consumer, Guid eventId, Func<Task> apply, CancellationToken cancellationToken)
        {
            if (processed.Contains(eventId)) return;
            await apply();
            processed.Add(eventId);
        }
    }

    private sealed class ProjectionStore : IScopeProjectionStore
    {
        public SeasonScope? Season { get; private set; }
        public WorkAreaScope? Area { get; private set; }
        public List<string> Order { get; } = [];
        public int Saves { get; private set; }
        public Task<SeasonScope?> GetSeasonAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Season);
        public Task<WorkAreaScope?> GetWorkAreaAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Area);
        public void Add(SeasonScope scope) { Season = scope; Order.Add("season"); }
        public void Add(WorkAreaScope scope)
        {
            Season.Should().NotBeNull();
            Area = scope;
            Order.Add("work-area");
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; return Task.CompletedTask; }
    }
}
