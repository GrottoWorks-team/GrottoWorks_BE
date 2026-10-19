using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;

namespace TaskExecution.UnitTests.Domain;

public sealed class ScopeProjectionTests
{
    [Fact]
    public void Season_scope_ignores_equal_or_older_versions()
    {
        var scope = SeasonScope.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15),
            SeasonScopeStatus.Active, 4, DateTimeOffset.UtcNow);

        var equalApplied = scope.Apply(
            Guid.NewGuid(), new DateOnly(2026, 9, 1), new DateOnly(2027, 2, 1),
            SeasonScopeStatus.Archived, 4, DateTimeOffset.UtcNow.AddMinutes(1));
        var olderApplied = scope.Apply(
            Guid.NewGuid(), new DateOnly(2026, 9, 1), new DateOnly(2027, 2, 1),
            SeasonScopeStatus.Archived, 3, DateTimeOffset.UtcNow.AddMinutes(1));

        equalApplied.Should().BeFalse();
        olderApplied.Should().BeFalse();
        scope.Status.Should().Be(SeasonScopeStatus.Active);
        scope.SourceVersion.Should().Be(4);
    }

    [Fact]
    public void Newer_season_snapshot_updates_scope_and_archived_state()
    {
        var parishId = Guid.NewGuid();
        var scope = SeasonScope.Create(
            Guid.NewGuid(), parishId, new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15),
            SeasonScopeStatus.Completed, 4, DateTimeOffset.UtcNow);

        var applied = scope.Apply(
            parishId, scope.StartDate, scope.EndDate,
            SeasonScopeStatus.Archived, 5, DateTimeOffset.UtcNow.AddMinutes(1));

        applied.Should().BeTrue();
        scope.IsReadOnly.Should().BeTrue();
        scope.SourceVersion.Should().Be(5);
    }

    [Fact]
    public void Work_area_scope_normalizes_name_and_applies_newer_snapshot()
    {
        var scope = WorkAreaScope.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  North facade  ",
            null, null, null, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5),
            WorkAreaScopeStatus.Planned, 1, DateTimeOffset.Now);
        var leaderId = Guid.NewGuid();

        var applied = scope.Apply(
            scope.SeasonId, scope.ParishId, "North facade", null, leaderId, null,
            scope.StartDate, scope.Deadline, WorkAreaScopeStatus.InProgress, 2, DateTimeOffset.Now);

        applied.Should().BeTrue();
        scope.Name.Should().Be("North facade");
        scope.LeaderUserId.Should().Be(leaderId);
        scope.Status.Should().Be(WorkAreaScopeStatus.InProgress);
        scope.UpdatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Invalid_scope_data_is_rejected()
    {
        var actSeason = () => SeasonScope.Create(
            Guid.Empty, Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1),
            SeasonScopeStatus.Planning, 0, DateTimeOffset.UtcNow);
        var actWorkArea = () => WorkAreaScope.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 151),
            null, null, null, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 4),
            WorkAreaScopeStatus.Planned, 1, DateTimeOffset.UtcNow);

        actSeason.Should().Throw<DomainRuleException>();
        actWorkArea.Should().Throw<DomainRuleException>();
    }
}
