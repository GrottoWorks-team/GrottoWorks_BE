using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.WorkAreas.Commands.UpdateWorkArea;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.UnitTests;

public sealed class UpdateWorkAreaHandlerTests
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
    public async Task Parish_user_updates_work_area_in_its_parish()
    {
        var workAreas = WorkAreas(BuildWorkArea());
        var category = BuildCategory();
        var handler = Handler(
            workAreas,
            Seasons(BuildSeason()),
            Categories(category),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new UpdateWorkAreaCommand(
            WorkAreaId,
            "  Updated area  ",
            true,
            "  Updated description  ",
            category.Id,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 12, 20),
            1));

        result.Outcome.Should().Be(UpdateWorkAreaOutcome.Success);
        var workArea = workAreas.Items.Single();
        workArea.Name.Should().Be("Updated area");
        workArea.Description.Should().Be("Updated description");
        workArea.WorkAreaCategoryId.Should().Be(category.Id);
        workArea.StartDate.Should().Be(new DateOnly(2026, 11, 1));
        workArea.Deadline.Should().Be(new DateOnly(2026, 12, 20));
        workArea.Version.Should().Be(2);
        workArea.UpdatedAt.Should().NotBeNull();
        result.WorkArea!.Version.Should().Be(2);
        workAreas.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Partial_update_preserves_omitted_fields_and_can_clear_description()
    {
        var originalCategoryId = Guid.NewGuid();
        var workArea = BuildWorkArea(categoryId: originalCategoryId);
        var workAreas = WorkAreas(workArea);
        var handler = Handler(
            workAreas,
            Seasons(BuildSeason()),
            Categories(),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new UpdateWorkAreaCommand(
            WorkAreaId,
            null,
            true,
            "   ",
            null,
            null,
            null,
            null));

        result.Outcome.Should().Be(UpdateWorkAreaOutcome.Success);
        workArea.Name.Should().Be("Original area");
        workArea.Description.Should().BeNull();
        workArea.WorkAreaCategoryId.Should().Be(originalCategoryId);
        workArea.StartDate.Should().Be(new DateOnly(2026, 10, 10));
        workArea.Deadline.Should().Be(new DateOnly(2026, 12, 10));
        workArea.CommunityId.Should().NotBeNull();
        workArea.LeaderUserId.Should().Be(LeaderId);
        workArea.Status.Should().Be(WorkAreaStatus.Planned);
    }

    [Fact]
    public async Task Admin_can_update_work_area_in_any_parish()
    {
        var result = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason(parishId: ParishSouth)),
                Categories(),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(Command(name: "Admin update"));

        result.Outcome.Should().Be(UpdateWorkAreaOutcome.Success);
        result.WorkArea.Should().NotBeNull();
    }

    [Fact]
    public async Task Assigned_leader_can_update_work_area()
    {
        var result = await Handler(
                WorkAreas(BuildWorkArea(leaderId: LeaderId)),
                Seasons(BuildSeason()),
                Categories(),
                User(GrottoWorksRoles.Leader, ParishNorth, LeaderId))
            .HandleAsync(Command(name: "Leader update"));

        result.Outcome.Should().Be(UpdateWorkAreaOutcome.Success);
    }

    [Fact]
    public async Task Unassigned_leader_is_rejected()
    {
        var handler = Handler(
            WorkAreas(BuildWorkArea(leaderId: Guid.NewGuid())),
            Seasons(BuildSeason()),
            Categories(),
            User(GrottoWorksRoles.Leader, ParishNorth, LeaderId));

        var action = () => handler.HandleAsync(Command());

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("WORK_AREA_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task User_from_another_parish_is_rejected()
    {
        var handler = Handler(
            WorkAreas(BuildWorkArea()),
            Seasons(BuildSeason()),
            Categories(),
            User(GrottoWorksRoles.Parish, ParishSouth));

        var action = () => handler.HandleAsync(Command());

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
            Categories(),
            new FakeCurrentUser { IsAuthenticated = false });
        var anonymousAction = () => anonymousHandler.HandleAsync(Command());
        var anonymousException = await anonymousAction.Should().ThrowAsync<DomainException>();

        var unsupportedHandler = Handler(
            WorkAreas(BuildWorkArea()),
            Seasons(BuildSeason()),
            Categories(),
            User(GrottoWorksRoles.Volunteer, ParishNorth));
        var unsupportedAction = () => unsupportedHandler.HandleAsync(Command());
        var unsupportedException = await unsupportedAction.Should().ThrowAsync<DomainException>();

        anonymousException.Which.Code.Should().Be("UNAUTHENTICATED");
        unsupportedException.Which.Code.Should().Be("WORK_AREA_ACCESS_DENIED");
    }

    [Fact]
    public async Task Missing_work_area_or_parent_season_returns_not_found_outcome()
    {
        var missingWorkArea = await Handler(
                WorkAreas(),
                Seasons(BuildSeason()),
                Categories(),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(Command());

        var missingSeason = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(),
                Categories(),
                User(GrottoWorksRoles.Admin))
            .HandleAsync(Command());

        missingWorkArea.Outcome.Should().Be(UpdateWorkAreaOutcome.NotFound);
        missingSeason.Outcome.Should().Be(UpdateWorkAreaOutcome.SeasonNotFound);
    }

    [Fact]
    public async Task Archived_season_is_read_only()
    {
        var workAreas = WorkAreas(BuildWorkArea());
        var result = await Handler(
                workAreas,
                Seasons(BuildSeason(SeasonStatus.Archived)),
                Categories(),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        result.Outcome.Should().Be(UpdateWorkAreaOutcome.SeasonInvalidState);
        workAreas.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Effective_dates_must_be_ordered_and_inside_parent_season()
    {
        var invalidOrderHandler = DefaultHandler();
        var invalidOrder = () => invalidOrderHandler.HandleAsync(Command(
            startDate: new DateOnly(2026, 12, 20),
            deadline: new DateOnly(2026, 11, 1)));
        var orderException = await invalidOrder.Should().ThrowAsync<DomainException>();

        var outsideSeasonHandler = DefaultHandler();
        var outsideSeason = () => outsideSeasonHandler.HandleAsync(Command(
            startDate: new DateOnly(2026, 9, 30)));
        var seasonException = await outsideSeason.Should().ThrowAsync<DomainException>();

        orderException.Which.Code.Should().Be("WORK_AREA_INVALID_DATE_RANGE");
        orderException.Which.StatusCode.Should().Be(422);
        seasonException.Which.Code.Should().Be("WORK_AREA_DATE_OUT_OF_SEASON");
        seasonException.Which.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Category_must_exist_and_be_active()
    {
        var missingResult = await DefaultHandler()
            .HandleAsync(Command(categoryId: Guid.NewGuid()));

        var inactiveCategory = BuildCategory(WorkAreaCategoryStatus.Inactive);
        var inactiveResult = await Handler(
                WorkAreas(BuildWorkArea()),
                Seasons(BuildSeason()),
                Categories(inactiveCategory),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(categoryId: inactiveCategory.Id));

        missingResult.Outcome.Should().Be(UpdateWorkAreaOutcome.CategoryNotFound);
        inactiveResult.Outcome.Should().Be(UpdateWorkAreaOutcome.CategoryInactive);
    }

    [Fact]
    public async Task Stale_version_and_database_concurrency_race_return_version_mismatch()
    {
        var staleRepository = WorkAreas(BuildWorkArea());
        var staleResult = await Handler(
                staleRepository,
                Seasons(BuildSeason()),
                Categories(),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command(expectedVersion: 2));

        var raceRepository = WorkAreas(BuildWorkArea());
        raceRepository.ThrowConcurrencyOnSave = true;
        var raceResult = await Handler(
                raceRepository,
                Seasons(BuildSeason()),
                Categories(),
                User(GrottoWorksRoles.Parish, ParishNorth))
            .HandleAsync(Command());

        staleResult.Outcome.Should().Be(UpdateWorkAreaOutcome.VersionMismatch);
        staleRepository.SaveChangesCount.Should().Be(0);
        raceResult.Outcome.Should().Be(UpdateWorkAreaOutcome.VersionMismatch);
    }

    [Fact]
    public void Request_validates_empty_blank_long_name_and_invalid_date_order()
    {
        Validate(new UpdateWorkAreaRequest()).Should().Contain(result =>
            result.ErrorMessage!.Contains("At least one field"));
        Validate(new UpdateWorkAreaRequest { Name = "   " }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateWorkAreaRequest.Name)));
        Validate(new UpdateWorkAreaRequest { Name = new string('n', 151) }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateWorkAreaRequest.Name)));
        Validate(new UpdateWorkAreaRequest
        {
            StartDate = new DateOnly(2026, 12, 1),
            Deadline = new DateOnly(2026, 11, 1)
        }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateWorkAreaRequest.Deadline)));
    }

    [Fact]
    public void Ef_model_maps_work_area_version_as_concurrency_token()
    {
        var options = new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new ParishCoordinationDbContext(options);
        var property = context.Model
            .FindEntityType(typeof(WorkArea))!
            .FindProperty(nameof(WorkArea.Version))!;

        property.GetColumnName().Should().Be("version");
        property.IsConcurrencyToken.Should().BeTrue();
        property.GetDefaultValue().Should().Be(1);
    }

    private static UpdateWorkAreaCommandHandler DefaultHandler() =>
        Handler(
            WorkAreas(BuildWorkArea()),
            Seasons(BuildSeason()),
            Categories(),
            User(GrottoWorksRoles.Parish, ParishNorth));

    private static UpdateWorkAreaCommand Command(
        string? name = "Updated area",
        Guid? categoryId = null,
        DateOnly? startDate = null,
        DateOnly? deadline = null,
        int? expectedVersion = null) =>
        new(
            WorkAreaId,
            name,
            false,
            null,
            categoryId,
            startDate,
            deadline,
            expectedVersion);

    private static UpdateWorkAreaCommandHandler Handler(
        FakeWorkAreaRepository workAreas,
        FakeSeasonRepository seasons,
        FakeWorkAreaCategoryRepository categories,
        FakeCurrentUser user) =>
        new(workAreas, seasons, categories, user);

    private static FakeCurrentUser User(
        string role,
        Guid? parishId = null,
        Guid? userId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId,
            UserId = userId ?? Guid.NewGuid()
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

    private static FakeWorkAreaCategoryRepository Categories(
        params WorkAreaCategory[] categories)
    {
        var repository = new FakeWorkAreaCategoryRepository();
        repository.Items.AddRange(categories);
        return repository;
    }

    private static WorkArea BuildWorkArea(
        Guid? categoryId = null,
        Guid? leaderId = null) =>
        WorkArea.Create(
            WorkAreaId,
            SeasonId,
            Guid.NewGuid(),
            categoryId,
            leaderId ?? LeaderId,
            Guid.NewGuid(),
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

    private static WorkAreaCategory BuildCategory(
        WorkAreaCategoryStatus status = WorkAreaCategoryStatus.Active) =>
        WorkAreaCategory.Create(
            Guid.NewGuid(),
            "LIGHTING",
            "Lighting",
            null,
            status);

    private static List<ValidationResult> Validate(UpdateWorkAreaRequest request)
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
