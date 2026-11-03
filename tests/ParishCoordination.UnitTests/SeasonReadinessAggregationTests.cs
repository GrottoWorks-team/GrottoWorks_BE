using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Events;

namespace ParishCoordination.UnitTests;

public sealed class SeasonReadinessAggregationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-03T12:00:00Z");

    [Fact]
    public void Season_requires_at_least_one_area_and_reopens_if_any_area_is_not_ready()
    {
        var season = Season(SeasonStatus.Active);
        season.ReconcileReadiness([], Now).Should().BeFalse();
        season.ReconcileReadiness([WorkAreaStatus.Ready, WorkAreaStatus.InProgress], Now).Should().BeFalse();
        season.ReconcileReadiness([WorkAreaStatus.Ready, WorkAreaStatus.Ready], Now).Should().BeTrue();
        season.Status.Should().Be(SeasonStatus.Ready);
        season.ReconcileReadiness([WorkAreaStatus.Ready], Now).Should().BeFalse();
        season.Version.Should().Be(2);
        season.ReconcileReadiness([WorkAreaStatus.NeedsCorrection], Now).Should().BeTrue();
        season.Status.Should().Be(SeasonStatus.Active);
    }

    [Theory]
    [InlineData(SeasonStatus.Planning)]
    [InlineData(SeasonStatus.Completed)]
    [InlineData(SeasonStatus.Archived)]
    public void Aggregation_does_not_change_non_active_lifecycle_states(SeasonStatus status)
    {
        var season = Season(status);
        season.ReconcileReadiness([WorkAreaStatus.Ready], Now).Should().BeFalse();
        season.Status.Should().Be(status);
        season.Version.Should().Be(1);
    }

    [Fact]
    public void Cascade_validates_all_areas_before_mutating_any()
    {
        var season = Season(SeasonStatus.Ready);
        var ready = Area(season.Id, WorkAreaStatus.Ready);
        var open = Area(season.Id, WorkAreaStatus.InProgress);
        season.Complete(Now);
        var complete = () => season.CompleteWorkAreas([ready, open], Now);
        complete.Should().Throw<InvalidOperationException>();
        ready.Status.Should().Be(WorkAreaStatus.Ready);
        ready.Version.Should().Be(1);
    }

    [Fact]
    public async Task Completing_season_stages_all_area_changes_and_outboxes_in_one_save()
    {
        using var db = Database();
        var season = Season(SeasonStatus.Planning);
        var first = Area(season.Id, WorkAreaStatus.Ready);
        var second = Area(season.Id, WorkAreaStatus.Ready);
        db.AddRange(season, first, second);
        await db.SaveChangesAsync();
        season.Activate(Now);
        await db.SaveChangesAsync();
        season.Status.Should().Be(SeasonStatus.Ready);
        season.Complete(Now);
        await db.SaveChangesAsync();
        first.Status.Should().Be(WorkAreaStatus.Completed);
        second.Status.Should().Be(WorkAreaStatus.Completed);
        var seasonMessage = (await db.PlanningEventOutbox.SingleAsync(row => row.EventType == "planning.season.completed")).ToMessage();
        seasonMessage.Payload.GetProperty("recipientUserIds").EnumerateArray().Select(x => x.GetGuid())
            .Should().BeEquivalentTo(new[] { first.LeaderUserId!.Value, first.MaterialOfficerUserId!.Value,
                second.LeaderUserId!.Value, second.MaterialOfficerUserId!.Value });
        db.PlanningEventOutbox.Should().Contain(row => row.AggregateId == first.Id && row.SourceVersion == first.Version);
        db.PlanningEventOutbox.Should().Contain(row => row.AggregateId == second.Id && row.SourceVersion == second.Version);
    }

    [Fact]
    public void Season_recipients_are_unique_current_role_holders_in_that_season_only()
    {
        var season = Season(SeasonStatus.Active);
        var first = Area(season.Id, WorkAreaStatus.Planned);
        var otherSeason = Area(Guid.NewGuid(), WorkAreaStatus.Planned);
        var payload = PlanningSnapshotMapper.SeasonPayload(PlanningSnapshotMapper.Season(season), [first, first, otherSeason]);
        payload.GetProperty("recipientUserIds").EnumerateArray().Select(x => x.GetGuid())
            .Should().BeEquivalentTo(new[] { first.LeaderUserId!.Value, first.MaterialOfficerUserId!.Value });
    }

    internal static ParishCoordinationDbContext Database() => new(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    internal static Season Season(SeasonStatus status) => ParishCoordination.Domain.Entities.Season.Create(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "Season", 2026, new(2026, 10, 1), new(2027, 1, 15), status, null, null, Now);
    internal static WorkArea Area(Guid seasonId, WorkAreaStatus status = WorkAreaStatus.Planned) => WorkArea.Create(
        seasonId, null, null, Guid.NewGuid(), Guid.NewGuid(), "Area", null, null, null, status, Now);
}
