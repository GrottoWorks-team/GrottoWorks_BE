using BuildingBlocks.Security;
using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Scope;

/// <summary>F-RES-01: projection idempotency and work-area access rules.</summary>
public sealed class WorkAreaScopeTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly FakeScopeStore _store = new();

    private static WorkAreaSnapshot Snapshot(Guid id, long version, Guid? officer = null, SeasonStatus season = SeasonStatus.Active) =>
        new(id, Guid.NewGuid(), Parish, " Hang đá ", WorkAreaStatus.Planned, season, null, null, officer, version);

    [Fact]
    public async Task Snapshot_creates_then_updates_and_ignores_stale_versions()
    {
        var handler = new ApplyWorkAreaSnapshotHandler(_store);
        var id = Guid.NewGuid();
        var officer = Guid.NewGuid();

        var (created, applied) = await handler.HandleAsync(Snapshot(id, 2));
        applied.Should().BeTrue();
        created.Name.Should().Be("Hang đá");

        (await handler.HandleAsync(Snapshot(id, 3, officer))).Applied.Should().BeTrue();
        var (current, staleApplied) = await handler.HandleAsync(Snapshot(id, 1));

        staleApplied.Should().BeFalse();
        current.MaterialOfficerUserId.Should().Be(officer);
        current.Version.Should().Be(3);
        _store.Scopes.Should().ContainSingle();
    }

    [Fact]
    public async Task Same_version_is_reapplied_idempotently()
    {
        var handler = new ApplyWorkAreaSnapshotHandler(_store);
        var id = Guid.NewGuid();
        await handler.HandleAsync(Snapshot(id, 4));

        (await handler.HandleAsync(Snapshot(id, 4))).Applied.Should().BeTrue();
    }

    [Fact]
    public async Task Season_status_moves_every_work_area_of_the_season()
    {
        var season = Guid.NewGuid();
        _store.Seed(Parish, null, season);
        _store.Seed(Parish, null, season);
        _store.Seed(Parish, null);

        var count = await new ApplySeasonStatusHandler(_store).HandleAsync(new SeasonStatusChange(season, SeasonStatus.Archived));

        count.Should().Be(2);
        _store.Scopes.Count(s => s.IsReadOnly).Should().Be(2);
    }

    [Fact]
    public async Task Read_is_limited_to_the_parish_and_admin_sees_all()
    {
        var scope = _store.Seed(Parish, null);
        var other = _store.Seed(Guid.NewGuid(), null);
        var access = new WorkAreaAccess(_store, new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Leader, Parish));

        (await access.ForReadAsync(scope.WorkAreaId)).Should().BeSameAs(scope);
        var hidden = (await FluentActions.Awaiting(() => access.ForReadAsync(other.WorkAreaId))
            .Should().ThrowAsync<DomainRuleException>()).Which;
        hidden.Code.Should().Be("WORK_AREA_NOT_FOUND");
        hidden.Kind.Should().Be(DomainErrorKind.NotFound);

        var admin = new WorkAreaAccess(_store, new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Admin));
        (await admin.ForReadAsync(other.WorkAreaId)).Should().BeSameAs(other);
    }

    [Fact]
    public async Task Write_requires_the_assigned_officer_and_a_non_archived_season()
    {
        var officer = Guid.NewGuid();
        var scope = _store.Seed(Parish, officer);
        var archived = _store.Seed(Parish, officer, seasonStatus: SeasonStatus.Archived);

        var mine = new WorkAreaAccess(_store, new FakeCurrentUser(officer, GrottoWorksRoles.MaterialOfficer, Parish));
        var otherOfficer = new WorkAreaAccess(_store, new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.MaterialOfficer, Parish));

        (await mine.ForMaterialOfficerWriteAsync(scope.WorkAreaId)).Should().BeSameAs(scope);
        (await FluentActions.Awaiting(() => otherOfficer.ForMaterialOfficerWriteAsync(scope.WorkAreaId))
            .Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        (await FluentActions.Awaiting(() => mine.ForMaterialOfficerWriteAsync(archived.WorkAreaId))
            .Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SEASON_READ_ONLY");
    }

    [Fact]
    public void RequireParish_uses_the_token_parish()
    {
        new WorkAreaAccess(_store, new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Parish, Parish))
            .RequireParish(Guid.NewGuid()).Should().Be(Parish, "non-admins cannot pick another parish");

        var act = () => new WorkAreaAccess(_store, new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Admin)).RequireParish();
        act.Should().Throw<DomainRuleException>().Which.Code.Should().Be("PARISH_SCOPE_REQUIRED");
    }

    [Fact]
    public async Task Parish_snapshot_without_season_status_takes_the_projected_season()
    {
        var season = Guid.NewGuid();
        await new ApplySeasonStatusHandler(_store).HandleAsync(new SeasonStatusChange(season, SeasonStatus.Active, 4));

        var (area, _) = await new ApplyWorkAreaSnapshotHandler(_store).HandleAsync(
            new WorkAreaSnapshot(Guid.NewGuid(), season, Parish, "Hang đá", WorkAreaStatus.InProgress, null, null, null, null, 1));

        area.SeasonStatus.Should().Be(SeasonStatus.Active);
    }

    [Fact]
    public async Task Stale_season_snapshot_is_ignored_and_unknown_season_defaults_to_planning()
    {
        var season = Guid.NewGuid();
        var handler = new ApplySeasonStatusHandler(_store);
        await handler.HandleAsync(new SeasonStatusChange(season, SeasonStatus.Archived, 5));
        (await handler.HandleAsync(new SeasonStatusChange(season, SeasonStatus.Active, 3))).Should().Be(0);
        _store.Seasons.Single().Status.Should().Be(SeasonStatus.Archived);

        var (orphan, _) = await new ApplyWorkAreaSnapshotHandler(_store).HandleAsync(
            new WorkAreaSnapshot(Guid.NewGuid(), Guid.NewGuid(), Parish, "Đường kiệu", WorkAreaStatus.Planned, null, null, null, null, 1));
        orphan.SeasonStatus.Should().Be(SeasonStatus.Planning);
    }

    [Fact]
    public async Task Update_without_season_status_keeps_the_stored_one()
    {
        var id = Guid.NewGuid();
        var handler = new ApplyWorkAreaSnapshotHandler(_store);
        var first = Snapshot(id, 1, season: SeasonStatus.Archived);
        await handler.HandleAsync(first);

        var (updated, _) = await handler.HandleAsync(first with { Version = 2, SeasonStatus = null, Name = "Mới" });

        updated.SeasonStatus.Should().Be(SeasonStatus.Archived);
    }
}
