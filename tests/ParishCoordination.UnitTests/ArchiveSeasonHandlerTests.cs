using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Api.Common;
using ParishCoordination.Application.Seasons.Commands.ArchiveSeason;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class ArchiveSeasonHandlerTests
{
    private static readonly Guid ParishNorth =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SeasonId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Parish_user_archives_completed_season_in_same_parish()
    {
        var season = BuildSeason();
        var originalName = season.Name;
        var originalYear = season.SeasonYear;
        var originalStartDate = season.StartDate;
        var originalEndDate = season.EndDate;
        var repository = Seasons(season);

        var result = await Handler(
                repository,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(expectedVersion: 1));

        result.Outcome.Should().Be(ArchiveSeasonOutcome.Success);
        result.Season!.Status.Should().Be(SeasonStatus.Archived);
        result.Season.Version.Should().Be(2);
        result.Season.UpdatedAt.Should().NotBeNull();
        result.Season.Name.Should().Be(originalName);
        result.Season.SeasonYear.Should().Be(originalYear);
        result.Season.StartDate.Should().Be(originalStartDate);
        result.Season.EndDate.Should().Be(originalEndDate);
        repository.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Admin_archives_completed_season_in_any_parish()
    {
        var season = BuildSeason(parishId: ParishSouth);

        var result = await Handler(
                Seasons(season),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(new ArchiveSeasonCommand(season.Id, null));

        result.Outcome.Should().Be(ArchiveSeasonOutcome.Success);
        result.Season!.ParishId.Should().Be(ParishSouth);
        result.Season.Status.Should().Be(SeasonStatus.Archived);
    }

    [Fact]
    public async Task Caller_from_another_parish_is_denied()
    {
        var action = () => Handler(
                Seasons(BuildSeason()),
                User(GrottoWorksRoles.Parish, ParishSouth))
            .HandleAsync(Command());

        (await action.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("PARISH_ACCESS_DENIED");
    }

    [Fact]
    public async Task Anonymous_and_unsupported_role_are_denied()
    {
        var anonymous = () => Handler(
                Seasons(BuildSeason()),
                new FakeCurrentUser { IsAuthenticated = false })
            .HandleAsync(Command());
        var unsupportedRole = () => Handler(
                Seasons(BuildSeason()),
                User(GrottoWorksRoles.Leader, ParishNorth))
            .HandleAsync(Command());

        (await anonymous.Should().ThrowAsync<DomainException>())
            .Which.StatusCode.Should().Be(401);
        (await unsupportedRole.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("SEASON_ACCESS_DENIED");
    }

    [Fact]
    public async Task Missing_season_returns_not_found()
    {
        var result = await Handler(
                Seasons(),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(ArchiveSeasonOutcome.NotFound);
    }

    [Theory]
    [InlineData(SeasonStatus.Planning)]
    [InlineData(SeasonStatus.Active)]
    [InlineData(SeasonStatus.Ready)]
    [InlineData(SeasonStatus.Archived)]
    public async Task Only_completed_season_can_be_archived(SeasonStatus status)
    {
        var repository = Seasons(BuildSeason(status: status));

        var result = await Handler(
                repository,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(ArchiveSeasonOutcome.InvalidState);
        repository.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Stale_version_and_database_concurrency_return_version_mismatch()
    {
        var staleRepository = Seasons(BuildSeason());
        var stale = await Handler(
                staleRepository,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(expectedVersion: 2));

        var raceRepository = Seasons(BuildSeason());
        raceRepository.ThrowConcurrencyOnSave = true;
        var race = await Handler(
                raceRepository,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(expectedVersion: 1));

        stale.Outcome.Should().Be(ArchiveSeasonOutcome.VersionMismatch);
        staleRepository.SaveChangesCount.Should().Be(0);
        race.Outcome.Should().Be(ArchiveSeasonOutcome.VersionMismatch);
    }

    [Fact]
    public void Version_precondition_rejects_invalid_header_and_disagreement()
    {
        SeasonVersionPrecondition.Resolve(null, "invalid").Error.Should()
            .Be(SeasonVersionPreconditionError.InvalidIfMatch);
        SeasonVersionPrecondition.Resolve(1, "2").Error.Should()
            .Be(SeasonVersionPreconditionError.VersionMismatch);
        SeasonVersionPrecondition.Resolve(2, "\"2\"").Version.Should().Be(2);
    }

    [Fact]
    public void Domain_rejects_archiving_when_season_is_not_completed()
    {
        var season = BuildSeason(status: SeasonStatus.Ready);

        var action = () => season.Archive(DateTimeOffset.UtcNow);

        action.Should().Throw<InvalidOperationException>();
        season.Status.Should().Be(SeasonStatus.Ready);
        season.Version.Should().Be(1);
        season.UpdatedAt.Should().BeNull();
    }

    private static ArchiveSeasonCommand Command(int? expectedVersion = null) =>
        new(SeasonId, expectedVersion);

    private static ArchiveSeasonCommandHandler Handler(
        FakeSeasonRepository seasons,
        FakeCurrentUser currentUser) =>
        new(seasons, currentUser);

    private static FakeSeasonRepository Seasons(params Season[] seasons)
    {
        var repository = new FakeSeasonRepository();
        repository.Items.AddRange(seasons);
        return repository;
    }

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId
        };

    private static Season BuildSeason(
        Guid? parishId = null,
        SeasonStatus status = SeasonStatus.Completed)
    {
        return Season.Create(
            SeasonId,
            parishId ?? ParishNorth,
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "Christmas 2026",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            status,
            "Annual Christmas season",
            100_000_000m,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
