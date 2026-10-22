using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaCommunity;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class AssignWorkAreaCommunityHandlerTests
{
    private static readonly Guid ParishNorth =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SeasonId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid WorkAreaId =
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid CommunityNorth =
        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid CommunitySouth =
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public async Task Parish_user_assigns_active_community_in_its_parish()
    {
        var workArea = BuildWorkArea();
        var workAreas = WorkAreas(workArea);
        var handler = Handler(
            workAreas,
            Seasons(BuildSeason()),
            Communities(BuildCommunity(CommunityNorth, ParishNorth)),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(Command(CommunityNorth, expectedVersion: 1));

        result.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.Success);
        workArea.CommunityId.Should().Be(CommunityNorth);
        workArea.Version.Should().Be(2);
        workArea.UpdatedAt.Should().NotBeNull();
        result.WorkArea!.CommunityId.Should().Be(CommunityNorth);
        result.WorkArea.Version.Should().Be(2);
        workAreas.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Assignment_preserves_unrelated_work_area_fields()
    {
        var categoryId = Guid.NewGuid();
        var leaderId = Guid.NewGuid();
        var officerId = Guid.NewGuid();
        var workArea = BuildWorkArea(
            categoryId: categoryId,
            leaderId: leaderId,
            officerId: officerId);
        var originalName = workArea.Name;
        var originalDescription = workArea.Description;
        var originalStartDate = workArea.StartDate;
        var originalDeadline = workArea.Deadline;
        var originalStatus = workArea.Status;

        var result = await Handler(
                WorkAreas(workArea),
                Seasons(BuildSeason()),
                Communities(BuildCommunity(CommunityNorth, ParishNorth)),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(CommunityNorth));

        result.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.Success);
        workArea.Name.Should().Be(originalName);
        workArea.Description.Should().Be(originalDescription);
        workArea.WorkAreaCategoryId.Should().Be(categoryId);
        workArea.LeaderUserId.Should().Be(leaderId);
        workArea.MaterialOfficerUserId.Should().Be(officerId);
        workArea.StartDate.Should().Be(originalStartDate);
        workArea.Deadline.Should().Be(originalDeadline);
        workArea.Status.Should().Be(originalStatus);
    }

    [Fact]
    public async Task Admin_can_assign_community_in_any_parish()
    {
        var result = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason(parishId: ParishSouth)),
                Communities(BuildCommunity(CommunitySouth, ParishSouth)),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(Command(CommunitySouth));

        result.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.Success);
        result.WorkArea!.CommunityId.Should().Be(CommunitySouth);
    }

    [Fact]
    public async Task User_from_another_parish_is_rejected()
    {
        var handler = Handler(
            WorkAreas(BuildWorkArea()),
            Seasons(BuildSeason()),
            Communities(BuildCommunity(CommunityNorth, ParishNorth)),
            User(GrottoWorksRoles.Parish, ParishSouth));

        var action = () => handler.HandleAsync(Command(CommunityNorth));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Anonymous_and_unsupported_roles_are_rejected()
    {
        var anonymousHandler = Handler(
            WorkAreas(BuildWorkArea()),
            Seasons(BuildSeason()),
            Communities(BuildCommunity(CommunityNorth, ParishNorth)),
            new FakeCurrentUser { IsAuthenticated = false });
        var anonymousAction = () => anonymousHandler.HandleAsync(Command(CommunityNorth));
        var anonymousException = await anonymousAction.Should().ThrowAsync<DomainException>();

        var unsupportedHandler = Handler(
            WorkAreas(BuildWorkArea()),
            Seasons(BuildSeason()),
            Communities(BuildCommunity(CommunityNorth, ParishNorth)),
            User(GrottoWorksRoles.Leader, ParishNorth));
        var unsupportedAction = () => unsupportedHandler.HandleAsync(Command(CommunityNorth));
        var unsupportedException = await unsupportedAction.Should().ThrowAsync<DomainException>();

        anonymousException.Which.Code.Should().Be("UNAUTHENTICATED");
        anonymousException.Which.StatusCode.Should().Be(401);
        unsupportedException.Which.Code.Should().Be("WORK_AREA_ACCESS_DENIED");
        unsupportedException.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Missing_work_area_season_or_community_returns_expected_outcome()
    {
        var missingWorkArea = await Handler(
                WorkAreas(),
                Seasons(BuildSeason()),
                Communities(BuildCommunity(CommunityNorth, ParishNorth)),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(Command(CommunityNorth));

        var missingSeason = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(),
                Communities(BuildCommunity(CommunityNorth, ParishNorth)),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(Command(CommunityNorth));

        var missingCommunity = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Communities(),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(Command(CommunityNorth));

        missingWorkArea.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.WorkAreaNotFound);
        missingSeason.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.SeasonNotFound);
        missingCommunity.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.CommunityNotFound);
    }

    [Fact]
    public async Task Community_from_another_parish_is_rejected_even_for_admin()
    {
        var handler = Handler(
            WorkAreas(BuildWorkArea()),
            Seasons(BuildSeason()),
            Communities(BuildCommunity(CommunitySouth, ParishSouth)),
            User(GrottoWorksRoles.Admin));

        var action = () => handler.HandleAsync(Command(CommunitySouth));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Inactive_community_cannot_be_assigned()
    {
        var result = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Communities(BuildCommunity(
                    CommunityNorth,
                    ParishNorth,
                    CommunityStatus.Inactive)),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(CommunityNorth));

        result.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.CommunityInactive);
    }

    [Fact]
    public async Task Archived_season_is_read_only()
    {
        var workAreas = WorkAreas(BuildWorkArea());
        var result = await Handler(
                workAreas,
                Seasons(BuildSeason(SeasonStatus.Archived)),
                Communities(BuildCommunity(CommunityNorth, ParishNorth)),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(CommunityNorth));

        result.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.SeasonInvalidState);
        workAreas.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Assigning_current_community_is_an_idempotent_no_op()
    {
        var workArea = BuildWorkArea(communityId: CommunityNorth);
        var workAreas = WorkAreas(workArea);
        var originalUpdatedAt = workArea.UpdatedAt;
        var handler = Handler(
            workAreas,
            Seasons(BuildSeason()),
            Communities(BuildCommunity(CommunityNorth, ParishNorth)),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(Command(CommunityNorth, expectedVersion: 1));

        result.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.Success);
        result.WorkArea!.Version.Should().Be(1);
        workArea.UpdatedAt.Should().Be(originalUpdatedAt);
        workAreas.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Null_community_unassigns_work_area_and_preserves_other_fields()
    {
        var categoryId = Guid.NewGuid();
        var leaderId = Guid.NewGuid();
        var officerId = Guid.NewGuid();
        var workArea = BuildWorkArea(
            communityId: CommunityNorth,
            categoryId: categoryId,
            leaderId: leaderId,
            officerId: officerId);
        var workAreas = WorkAreas(workArea);
        var originalName = workArea.Name;
        var originalDescription = workArea.Description;
        var originalStartDate = workArea.StartDate;
        var originalDeadline = workArea.Deadline;
        var originalStatus = workArea.Status;

        var result = await Handler(
                workAreas,
                Seasons(BuildSeason()),
                Communities(),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(null, expectedVersion: 1));

        result.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.Success);
        result.WorkArea!.CommunityId.Should().BeNull();
        workArea.CommunityId.Should().BeNull();
        workArea.Version.Should().Be(2);
        workArea.UpdatedAt.Should().NotBeNull();
        workAreas.SaveChangesCount.Should().Be(1);
        workArea.Name.Should().Be(originalName);
        workArea.Description.Should().Be(originalDescription);
        workArea.WorkAreaCategoryId.Should().Be(categoryId);
        workArea.LeaderUserId.Should().Be(leaderId);
        workArea.MaterialOfficerUserId.Should().Be(officerId);
        workArea.StartDate.Should().Be(originalStartDate);
        workArea.Deadline.Should().Be(originalDeadline);
        workArea.Status.Should().Be(originalStatus);
    }

    [Fact]
    public async Task Unassigning_work_area_without_community_is_an_idempotent_no_op()
    {
        var workArea = BuildWorkArea();
        var workAreas = WorkAreas(workArea);
        var originalUpdatedAt = workArea.UpdatedAt;

        var result = await Handler(
                workAreas,
                Seasons(BuildSeason()),
                Communities(),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(null, expectedVersion: 1));

        result.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.Success);
        result.WorkArea!.CommunityId.Should().BeNull();
        workArea.Version.Should().Be(1);
        workArea.UpdatedAt.Should().Be(originalUpdatedAt);
        workAreas.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Stale_version_and_database_concurrency_race_return_version_mismatch()
    {
        var staleRepository = WorkAreas(BuildWorkArea());
        var staleResult = await Handler(
                staleRepository,
                Seasons(BuildSeason()),
                Communities(BuildCommunity(CommunityNorth, ParishNorth)),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(CommunityNorth, expectedVersion: 2));

        var raceRepository = WorkAreas(BuildWorkArea());
        raceRepository.ThrowConcurrencyOnSave = true;
        var raceResult = await Handler(
                raceRepository,
                Seasons(BuildSeason()),
                Communities(BuildCommunity(CommunityNorth, ParishNorth)),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(CommunityNorth));

        staleResult.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.VersionMismatch);
        staleRepository.SaveChangesCount.Should().Be(0);
        raceResult.Outcome.Should().Be(AssignWorkAreaCommunityOutcome.VersionMismatch);
    }

    [Fact]
    public void Request_requires_property_but_allows_null_to_unassign()
    {
        Validate(new AssignWorkAreaCommunityRequest()).Should().Contain(result =>
            result.MemberNames.Contains(nameof(AssignWorkAreaCommunityRequest.CommunityId)));
        Validate(new AssignWorkAreaCommunityRequest { CommunityId = null }).Should().BeEmpty();
        Validate(new AssignWorkAreaCommunityRequest { CommunityId = Guid.Empty }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(AssignWorkAreaCommunityRequest.CommunityId)));
        Validate(new AssignWorkAreaCommunityRequest { CommunityId = CommunityNorth }).Should().BeEmpty();
    }

    private static AssignWorkAreaCommunityCommand Command(
        Guid? communityId,
        int? expectedVersion = null) =>
        new(WorkAreaId, communityId, expectedVersion);

    private static AssignWorkAreaCommunityCommandHandler Handler(
        FakeWorkAreaRepository workAreas,
        FakeSeasonRepository seasons,
        FakeCommunityRepository communities,
        FakeCurrentUser user) =>
        new(workAreas, seasons, communities, user);

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId
        };

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

    private static FakeCommunityRepository Communities(params Community[] communities)
    {
        var repository = new FakeCommunityRepository();
        repository.Items.AddRange(communities);
        return repository;
    }

    private static WorkArea BuildWorkArea(
        Guid? communityId = null,
        Guid? categoryId = null,
        Guid? leaderId = null,
        Guid? officerId = null) =>
        WorkArea.Create(
            WorkAreaId,
            SeasonId,
            communityId,
            categoryId,
            leaderId,
            officerId,
            "Original area",
            "Original description",
            new DateOnly(2026, 10, 10),
            new DateOnly(2026, 12, 10),
            WorkAreaStatus.Planned,
            DateTimeOffset.UtcNow);

    private static Season BuildSeason(
        SeasonStatus status = SeasonStatus.Planning,
        Guid? parishId = null) =>
        Season.Create(
            SeasonId,
            parishId ?? ParishNorth,
            Guid.NewGuid(),
            "Season 2026",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            status,
            null,
            null,
            DateTimeOffset.UtcNow);

    private static Community BuildCommunity(
        Guid communityId,
        Guid parishId,
        CommunityStatus status = CommunityStatus.Active)
    {
        var community = Community.Create(
            communityId,
            parishId,
            "Community",
            null,
            DateTimeOffset.UtcNow);

        if (status is CommunityStatus.Inactive)
        {
            community.Update(
                community.Name,
                community.Description,
                CommunityStatus.Inactive,
                DateTimeOffset.UtcNow);
        }

        return community;
    }

    private static List<ValidationResult> Validate(AssignWorkAreaCommunityRequest request)
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
