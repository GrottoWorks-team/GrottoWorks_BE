using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain.Scopes;

namespace TaskExecution.UnitTests.Application;

public sealed class ApplyScopeSnapshotHandlerTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Old_or_equal_snapshot_keeps_all_current_fields_and_runs_inside_transaction(long incomingVersion)
    {
        var leader = Guid.NewGuid();
        var existing = WorkAreaScope.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Current",
            Guid.NewGuid(), leader, Guid.NewGuid(), new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 20),
            WorkAreaScopeStatus.Planned, 3, DateTimeOffset.UtcNow);
        var transaction = new FakeTransaction();
        var store = new FakeScopeProjectionStore(workArea: existing);
        var handler = new ApplyWorkAreaScopeSnapshotHandler(store, transaction);
        var result = await handler.HandleAsync(new WorkAreaScopeSnapshot(existing.WorkAreaId,
            existing.SeasonId, existing.ParishId, "Obsolete", null, null, null, null, null,
            WorkAreaScopeStatus.Completed, incomingVersion));

        result.Applied.Should().BeFalse();
        result.Scope.Name.Should().Be("Current");
        result.Scope.LeaderUserId.Should().Be(leader);
        existing.SourceVersion.Should().Be(3);
        transaction.ScopeType.Should().Be("work-area");
        transaction.ScopeId.Should().Be(existing.WorkAreaId);
        store.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task New_season_snapshot_is_added_and_saved()
    {
        var store = new FakeScopeProjectionStore();
        var handler = new ApplySeasonScopeSnapshotHandler(store, new FakeTransaction());
        var snapshot = new SeasonScopeSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15),
            SeasonScopeStatus.Planning, 1);

        var result = await handler.HandleAsync(snapshot);

        result.Applied.Should().BeTrue();
        result.Scope.Id.Should().Be(snapshot.SeasonId);
        store.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Replayed_work_area_version_is_idempotent()
    {
        var existing = WorkAreaScope.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Area", null, null, null,
            null, null, WorkAreaScopeStatus.Planned, 3, DateTimeOffset.UtcNow);
        var store = new FakeScopeProjectionStore(workArea: existing);
        var handler = new ApplyWorkAreaScopeSnapshotHandler(store, new FakeTransaction());
        var snapshot = new WorkAreaScopeSnapshot(
            existing.WorkAreaId, existing.SeasonId, existing.ParishId, "Changed",
            null, null, null, null, null, WorkAreaScopeStatus.Completed, 3);

        var result = await handler.HandleAsync(snapshot);

        result.Applied.Should().BeFalse();
        result.Scope.Name.Should().Be("Area");
        store.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Newer_work_area_version_is_saved()
    {
        var existing = WorkAreaScope.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Area", null, null, null,
            null, null, WorkAreaScopeStatus.Planned, 1, DateTimeOffset.UtcNow);
        var store = new FakeScopeProjectionStore(workArea: existing);
        var handler = new ApplyWorkAreaScopeSnapshotHandler(store, new FakeTransaction());
        var snapshot = new WorkAreaScopeSnapshot(
            existing.WorkAreaId, existing.SeasonId, existing.ParishId, "Updated Area",
            null, Guid.NewGuid(), null, null, null, WorkAreaScopeStatus.InProgress, 2);

        var result = await handler.HandleAsync(snapshot);

        result.Applied.Should().BeTrue();
        result.Scope.Name.Should().Be("Updated Area");
        store.SaveCalls.Should().Be(1);
    }

    private sealed class FakeScopeProjectionStore(
        SeasonScope? season = null,
        WorkAreaScope? workArea = null) : IScopeProjectionStore
    {
        private SeasonScope? _season = season;
        private WorkAreaScope? _workArea = workArea;

        public int SaveCalls { get; private set; }

        public Task<SeasonScope?> GetSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_season?.SeasonId == seasonId ? _season : null);

        public Task<WorkAreaScope?> GetWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_workArea?.WorkAreaId == workAreaId ? _workArea : null);

        public void Add(SeasonScope scope) => _season = scope;
        public void Add(WorkAreaScope scope) => _workArea = scope;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransaction : IScopeSnapshotTransaction
    {
        public string? ScopeType { get; private set; }
        public Guid ScopeId { get; private set; }
        public Task<T> ExecuteAsync<T>(string scopeType, Guid scopeId, Func<Task<T>> apply,
            CancellationToken cancellationToken = default)
        {
            ScopeType = scopeType;
            ScopeId = scopeId;
            return apply();
        }
    }
}
