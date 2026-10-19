using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.Users;
using ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaLeader;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class AssignWorkAreaLeaderHandlerTests
{
    private static readonly Guid ParishNorth =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SeasonId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid WorkAreaId =
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid LeaderId =
        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task Parish_user_assigns_active_leader_from_same_parish()
    {
        var workArea = BuildWorkArea();
        var workAreas = WorkAreas(workArea);
        var lookup = Users(Leader());
        var handler = Handler(
            workAreas,
            Seasons(BuildSeason()),
            lookup,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(Command());

        result.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.Success);
        result.WorkArea!.LeaderId.Should().Be(LeaderId);
        result.WorkArea.Version.Should().Be(2);
        workArea.LeaderUserId.Should().Be(LeaderId);
        workArea.UpdatedAt.Should().NotBeNull();
        workAreas.SaveChangesCount.Should().Be(1);
        lookup.LastUserId.Should().Be(LeaderId);
    }

    [Fact]
    public async Task Admin_can_assign_leader_for_any_parish_but_target_must_match_season()
    {
        var success = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Users(Leader()),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(Command());

        var wrongParish = () => Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Users(Leader(parishId: ParishSouth)),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(Command());

        success.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.Success);
        (await wrongParish.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("PARISH_ACCESS_DENIED");
    }

    [Fact]
    public async Task Caller_from_another_parish_and_wrong_role_are_denied()
    {
        var otherParish = () => Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Users(Leader()),
                User(GrottoWorksRoles.Parish, ParishSouth))
            .HandleAsync(Command());

        var wrongRole = () => Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Users(Leader()),
                User(GrottoWorksRoles.Leader, ParishNorth))
            .HandleAsync(Command());

        (await otherParish.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        (await wrongRole.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("WORK_AREA_ACCESS_DENIED");
    }

    [Theory]
    [InlineData(IdentityUserStatus.Locked)]
    [InlineData(IdentityUserStatus.Inactive)]
    public async Task Non_active_user_is_rejected(IdentityUserStatus status)
    {
        var result = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Users(Leader(status: status)),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.UserNotActive);
    }

    [Fact]
    public async Task Missing_user_and_wrong_role_are_rejected()
    {
        var missing = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Users(),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        var wrongRole = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Users(Leader(role: GrottoWorksRoles.Volunteer)),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        missing.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.UserNotFound);
        wrongRole.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.UserRoleMismatch);
    }

    [Fact]
    public async Task Missing_work_area_or_season_is_reported_before_identity_lookup()
    {
        var lookup = Users(Leader());
        var missingWorkArea = await Handler(
                WorkAreas(),
                Seasons(BuildSeason()),
                lookup,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        var missingSeason = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(),
                lookup,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        missingWorkArea.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.WorkAreaNotFound);
        missingSeason.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.SeasonNotFound);
        lookup.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Archived_season_is_rejected_before_identity_lookup()
    {
        var lookup = Users(Leader());
        var result = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason(SeasonStatus.Archived)),
                lookup,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.SeasonInvalidState);
        lookup.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Identity_outage_is_reported_without_mutating_work_area()
    {
        var workArea = BuildWorkArea();
        var workAreas = WorkAreas(workArea);
        var lookup = Users();
        lookup.ThrowUnavailable = true;

        var result = await Handler(
                workAreas,
                Seasons(BuildSeason()),
                lookup,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.IdentityServiceUnavailable);
        workArea.LeaderUserId.Should().BeNull();
        workAreas.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Assigning_current_leader_is_an_idempotent_no_op_after_validation()
    {
        var workArea = BuildWorkArea(LeaderId);
        var workAreas = WorkAreas(workArea);
        var lookup = Users(Leader());
        var originalUpdatedAt = workArea.UpdatedAt;

        var result = await Handler(
                workAreas,
                Seasons(BuildSeason()),
                lookup,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(expectedVersion: 1));

        result.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.Success);
        result.WorkArea!.Version.Should().Be(1);
        workArea.UpdatedAt.Should().Be(originalUpdatedAt);
        workAreas.SaveChangesCount.Should().Be(0);
        lookup.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Stale_version_and_database_concurrency_race_return_version_mismatch()
    {
        var staleLookup = Users(Leader());
        var staleRepository = WorkAreas(BuildWorkArea());
        var stale = await Handler(
                staleRepository,
                Seasons(BuildSeason()),
                staleLookup,
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(expectedVersion: 2));

        var raceRepository = WorkAreas(BuildWorkArea());
        raceRepository.ThrowConcurrencyOnSave = true;
        var race = await Handler(
                raceRepository,
                Seasons(BuildSeason()),
                Users(Leader()),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        stale.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.VersionMismatch);
        staleLookup.CallCount.Should().Be(0);
        staleRepository.SaveChangesCount.Should().Be(0);
        race.Outcome.Should().Be(AssignWorkAreaLeaderOutcome.VersionMismatch);
    }

    [Fact]
    public void Request_requires_non_empty_user_id()
    {
        Validate(new AssignUserRequest()).Should().Contain(result =>
            result.MemberNames.Contains(nameof(AssignUserRequest.UserId)));
        Validate(new AssignUserRequest { UserId = Guid.Empty }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(AssignUserRequest.UserId)));
        Validate(new AssignUserRequest { UserId = LeaderId }).Should().BeEmpty();
    }

    private static AssignWorkAreaLeaderCommand Command(int? expectedVersion = null) =>
        new(WorkAreaId, LeaderId, expectedVersion);

    private static AssignWorkAreaLeaderCommandHandler Handler(
        FakeWorkAreaRepository workAreas,
        FakeSeasonRepository seasons,
        FakeIdentityUserLookup users,
        FakeCurrentUser currentUser) =>
        new(workAreas, seasons, users, currentUser);

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId
        };

    private static FakeIdentityUserLookup Users(params IdentityUser[] users)
    {
        var lookup = new FakeIdentityUserLookup();
        lookup.Items.AddRange(users);
        return lookup;
    }

    private static IdentityUser Leader(
        IdentityUserStatus status = IdentityUserStatus.Active,
        string role = GrottoWorksRoles.Leader,
        Guid? parishId = null) =>
        new(LeaderId, status, role, parishId ?? ParishNorth);

    private static FakeWorkAreaRepository WorkAreas(params WorkArea[] workAreas)
    {
        var repository = new FakeWorkAreaRepository();
        repository.Items.AddRange(workAreas);
        return repository;
    }

    private static FakeSeasonRepository Seasons(params Season[] seasons)
    {
        var repository = new FakeSeasonRepository();
        repository.Items.AddRange(seasons);
        return repository;
    }

    private static WorkArea BuildWorkArea(Guid? leaderId = null) =>
        WorkArea.Create(
            WorkAreaId,
            SeasonId,
            null,
            Guid.NewGuid(),
            leaderId,
            Guid.NewGuid(),
            "Original area",
            "Original description",
            new DateOnly(2026, 10, 10),
            new DateOnly(2026, 12, 10),
            WorkAreaStatus.Planned,
            DateTimeOffset.UtcNow);

    private static Season BuildSeason(SeasonStatus status = SeasonStatus.Planning) =>
        Season.Create(
            SeasonId,
            ParishNorth,
            Guid.NewGuid(),
            "Season 2026",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            status,
            null,
            null,
            DateTimeOffset.UtcNow);

    private static List<ValidationResult> Validate(AssignUserRequest request)
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
