using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Api.Common;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.Seasons.Commands.ActivateSeason;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class ActivateSeasonHandlerTests
{
    private static readonly Guid ParishNorth =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SeasonId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Parish_user_activates_ready_planning_season()
    {
        var season = BuildSeason();
        var originalName = season.Name;
        var originalStartDate = season.StartDate;
        var originalEndDate = season.EndDate;
        var seasons = Seasons(season);
        var workAreas = WorkAreas(BuildAssignedWorkArea());

        var result = await Handler(
                seasons,
                workAreas,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(expectedVersion: 1));

        result.Outcome.Should().Be(ActivateSeasonOutcome.Success);
        result.Season!.Status.Should().Be(SeasonStatus.Active);
        result.Season.Version.Should().Be(2);
        result.Season.UpdatedAt.Should().NotBeNull();
        result.Season.Name.Should().Be(originalName);
        result.Season.StartDate.Should().Be(originalStartDate);
        result.Season.EndDate.Should().Be(originalEndDate);
        seasons.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Admin_can_activate_season_in_any_parish()
    {
        var season = BuildSeason(parishId: ParishSouth);

        var result = await Handler(
                Seasons(season),
                WorkAreas(BuildAssignedWorkArea(season.Id)),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(new ActivateSeasonCommand(season.Id, null));

        result.Outcome.Should().Be(ActivateSeasonOutcome.Success);
        result.Season!.ParishId.Should().Be(ParishSouth);
    }

    [Fact]
    public async Task Caller_from_another_parish_and_wrong_role_are_denied()
    {
        var otherParish = () => Handler(
                Seasons(BuildSeason()),
                WorkAreas(BuildAssignedWorkArea()),
                User(GrottoWorksRoles.Parish, ParishSouth))
            .HandleAsync(Command());

        var wrongRole = () => Handler(
                Seasons(BuildSeason()),
                WorkAreas(BuildAssignedWorkArea()),
                User(GrottoWorksRoles.Leader, ParishNorth))
            .HandleAsync(Command());

        (await otherParish.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        (await wrongRole.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("SEASON_ACCESS_DENIED");
    }

    [Fact]
    public async Task Anonymous_user_is_rejected()
    {
        var action = () => Handler(
                Seasons(BuildSeason()),
                WorkAreas(BuildAssignedWorkArea()),
                new FakeCurrentUser { IsAuthenticated = false })
            .HandleAsync(Command());

        (await action.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("UNAUTHENTICATED");
    }

    [Fact]
    public async Task Unknown_season_returns_not_found()
    {
        var result = await Handler(
                Seasons(),
                WorkAreas(BuildAssignedWorkArea()),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(ActivateSeasonOutcome.NotFound);
    }

    [Theory]
    [InlineData(SeasonStatus.Active)]
    [InlineData(SeasonStatus.Ready)]
    [InlineData(SeasonStatus.Completed)]
    [InlineData(SeasonStatus.Archived)]
    public async Task Only_planning_season_can_be_activated(SeasonStatus status)
    {
        var seasons = Seasons(BuildSeason(status: status));

        var result = await Handler(
                seasons,
                WorkAreas(BuildAssignedWorkArea()),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(ActivateSeasonOutcome.InvalidState);
        seasons.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Season_requires_at_least_one_work_area()
    {
        var result = await Handler(
                Seasons(BuildSeason()),
                WorkAreas(),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(ActivateSeasonOutcome.WorkAreaRequired);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Every_work_area_requires_leader_and_material_officer(
        bool hasLeader,
        bool hasMaterialOfficer)
    {
        var workArea = BuildWorkArea(
            hasLeader ? Guid.NewGuid() : null,
            hasMaterialOfficer ? Guid.NewGuid() : null);

        var result = await Handler(
                Seasons(BuildSeason()),
                WorkAreas(BuildAssignedWorkArea(), workArea),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(ActivateSeasonOutcome.WorkAreaAssignmentIncomplete);
    }

    [Fact]
    public async Task Community_and_category_are_optional_for_activation()
    {
        var result = await Handler(
                Seasons(BuildSeason()),
                WorkAreas(BuildAssignedWorkArea()),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(ActivateSeasonOutcome.Success);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-10-01")]
    [InlineData("2027-01-15", "2027-12-31")]
    [InlineData("2026-11-01", "2026-12-01")]
    public async Task Active_period_overlap_in_same_parish_is_rejected(
        string otherStart,
        string otherEnd)
    {
        var target = BuildSeason();
        var overlapping = BuildSeason(
            id: Guid.NewGuid(),
            status: SeasonStatus.Active,
            seasonYear: 2025,
            startDate: DateOnly.Parse(otherStart),
            endDate: DateOnly.Parse(otherEnd));

        var result = await Handler(
                Seasons(target, overlapping),
                WorkAreas(BuildAssignedWorkArea()),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(ActivateSeasonOutcome.ActivePeriodConflict);
    }

    [Fact]
    public async Task Overlap_ignores_other_parishes_and_non_active_seasons()
    {
        var target = BuildSeason();
        var otherParish = BuildSeason(
            id: Guid.NewGuid(),
            parishId: ParishSouth,
            status: SeasonStatus.Active);
        var completed = BuildSeason(
            id: Guid.NewGuid(),
            status: SeasonStatus.Completed,
            seasonYear: 2025);

        var result = await Handler(
                Seasons(target, otherParish, completed),
                WorkAreas(BuildAssignedWorkArea()),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(ActivateSeasonOutcome.Success);
    }

    [Fact]
    public async Task Stale_version_and_database_concurrency_race_return_version_mismatch()
    {
        var staleRepository = Seasons(BuildSeason());
        var stale = await Handler(
                staleRepository,
                WorkAreas(BuildAssignedWorkArea()),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(expectedVersion: 2));

        var raceRepository = Seasons(BuildSeason());
        raceRepository.ThrowConcurrencyOnSave = true;
        var race = await Handler(
                raceRepository,
                WorkAreas(BuildAssignedWorkArea()),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        stale.Outcome.Should().Be(ActivateSeasonOutcome.VersionMismatch);
        staleRepository.SaveChangesCount.Should().Be(0);
        race.Outcome.Should().Be(ActivateSeasonOutcome.VersionMismatch);
    }

    [Fact]
    public void Version_request_rejects_non_positive_values()
    {
        Validate(new VersionedCommandRequest { Version = 0 }).Should().NotBeEmpty();
        Validate(new VersionedCommandRequest { Version = 1 }).Should().BeEmpty();
        Validate(new VersionedCommandRequest()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(2, null, 2)]
    [InlineData(null, "2", 2)]
    [InlineData(null, "\"2\"", 2)]
    [InlineData(2, "\"2\"", 2)]
    public void Version_precondition_accepts_body_or_header(
        int? bodyVersion,
        string? ifMatch,
        int? expectedVersion)
    {
        var result = SeasonVersionPrecondition.Resolve(bodyVersion, ifMatch);

        result.Error.Should().Be(SeasonVersionPreconditionError.None);
        result.Version.Should().Be(expectedVersion);
    }

    [Fact]
    public void Version_precondition_reports_invalid_header_and_body_header_mismatch()
    {
        SeasonVersionPrecondition.Resolve(null, "invalid").Error.Should()
            .Be(SeasonVersionPreconditionError.InvalidIfMatch);
        SeasonVersionPrecondition.Resolve(1, "2").Error.Should()
            .Be(SeasonVersionPreconditionError.VersionMismatch);
    }

    private static ActivateSeasonCommand Command(int? expectedVersion = null) =>
        new(SeasonId, expectedVersion);

    private static ActivateSeasonCommandHandler Handler(
        FakeSeasonRepository seasons,
        FakeWorkAreaRepository workAreas,
        FakeCurrentUser currentUser) =>
        new(seasons, workAreas, currentUser);

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId
        };

    private static FakeSeasonRepository Seasons(params Season[] seasons)
    {
        var repository = new FakeSeasonRepository();
        repository.Items.AddRange(seasons);
        return repository;
    }

    private static FakeWorkAreaRepository WorkAreas(params WorkArea[] workAreas)
    {
        var repository = new FakeWorkAreaRepository();
        repository.Items.AddRange(workAreas);
        return repository;
    }

    private static Season BuildSeason(
        Guid? id = null,
        Guid? parishId = null,
        SeasonStatus status = SeasonStatus.Planning,
        int seasonYear = 2026,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        var seasonId = id ?? SeasonId;

        return Season.Create(
            seasonId,
            parishId ?? ParishNorth,
            Guid.NewGuid(),
            $"Season {seasonYear} {seasonId:N}",
            seasonYear,
            startDate ?? new DateOnly(2026, 10, 1),
            endDate ?? new DateOnly(2027, 1, 15),
            status,
            "Season description",
            100m,
            DateTimeOffset.UtcNow);
    }

    private static WorkArea BuildAssignedWorkArea(Guid? seasonId = null) =>
        BuildWorkArea(Guid.NewGuid(), Guid.NewGuid(), seasonId);

    private static WorkArea BuildWorkArea(
        Guid? leaderId,
        Guid? materialOfficerId,
        Guid? seasonId = null) =>
        WorkArea.Create(
            Guid.NewGuid(),
            seasonId ?? SeasonId,
            null,
            null,
            leaderId,
            materialOfficerId,
            "Work area",
            null,
            new DateOnly(2026, 10, 10),
            new DateOnly(2026, 12, 20),
            WorkAreaStatus.Planned,
            DateTimeOffset.UtcNow);

    private static List<ValidationResult> Validate(VersionedCommandRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);
        return results;
    }
}
