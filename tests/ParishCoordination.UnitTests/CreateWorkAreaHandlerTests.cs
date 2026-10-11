using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Application.WorkAreas.Commands.CreateWorkArea;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class CreateWorkAreaHandlerTests
{
    private static readonly Guid ParishNorth = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SeasonNorth = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CommunityNorth = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");
    private static readonly Guid CommunitySouth = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000010");

    [Fact]
    public async Task Parish_user_can_create_work_area_in_current_season()
    {
        var workAreas = new FakeWorkAreaRepository();
        var handler = Handler(workAreas, User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new CreateWorkAreaCommand(
            SeasonNorth,
            "  Lighting Area  ",
            "  Description  ",
            Guid.NewGuid(),
            CommunityNorth,
            new DateOnly(2026, 10, 10),
            new DateOnly(2026, 12, 10)));

        result.Outcome.Should().Be(CreateWorkAreaOutcome.Success);
        workAreas.Items.Should().ContainSingle();
        var workArea = workAreas.Items.Single();
        workArea.Id.Should().NotBe(Guid.Empty);
        workArea.Name.Should().Be("Lighting Area");
        workArea.Description.Should().Be("Description");
        workArea.WorkAreaCategoryId.Should().NotBeNull();
        workArea.Status.Should().Be(WorkAreaStatus.Planned);
        workArea.CommunityId.Should().Be(CommunityNorth);
        workArea.LeaderUserId.Should().BeNull();
        workArea.MaterialOfficerUserId.Should().BeNull();
        result.WorkArea!.Id.Should().Be(workArea.Id);
    }

    [Fact]
    public async Task Admin_can_create_work_area_for_any_parish_season()
    {
        var seasons = Seasons();
        seasons.Items.Add(Season.Create(
            Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            ParishSouth,
            Guid.NewGuid(),
            "South Season",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            SeasonStatus.Planning,
            null,
            null,
            DateTimeOffset.UtcNow));

        var handler = new CreateWorkAreaCommandHandler(
            seasons,
            Communities(),
            new FakeWorkAreaRepository(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new CreateWorkAreaCommand(
            Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            "South Area",
            null,
            null,
            CommunitySouth,
            null,
            null));

        result.Outcome.Should().Be(CreateWorkAreaOutcome.Success);
    }

    [Fact]
    public async Task Anonymous_user_is_rejected()
    {
        var handler = Handler(new FakeWorkAreaRepository(), new FakeCurrentUser { IsAuthenticated = false });

        var action = () => handler.HandleAsync(Command());

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("UNAUTHENTICATED");
        exception.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task User_from_another_parish_is_rejected()
    {
        var handler = Handler(new FakeWorkAreaRepository(), User(GrottoWorksRoles.Parish, ParishSouth));

        var action = () => handler.HandleAsync(Command());

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Missing_season_returns_not_found_outcome()
    {
        var handler = new CreateWorkAreaCommandHandler(
            new FakeSeasonRepository(),
            Communities(),
            new FakeWorkAreaRepository(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(Command(Guid.NewGuid()));

        result.Outcome.Should().Be(CreateWorkAreaOutcome.SeasonNotFound);
    }

    [Theory]
    [InlineData(SeasonStatus.Active)]
    [InlineData(SeasonStatus.Ready)]
    [InlineData(SeasonStatus.Completed)]
    [InlineData(SeasonStatus.Archived)]
    public async Task Non_planning_season_status_is_rejected(SeasonStatus status)
    {
        var seasons = Seasons(status);
        var handler = new CreateWorkAreaCommandHandler(
            seasons,
            Communities(),
            new FakeWorkAreaRepository(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(Command());

        result.Outcome.Should().Be(CreateWorkAreaOutcome.SeasonInvalidState);
    }

    [Fact]
    public async Task Community_not_found_returns_not_found_outcome()
    {
        var handler = Handler(new FakeWorkAreaRepository(), User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(Command(communityId: Guid.NewGuid()));

        result.Outcome.Should().Be(CreateWorkAreaOutcome.CommunityNotFound);
    }

    [Fact]
    public async Task Community_from_another_parish_is_rejected()
    {
        var handler = Handler(new FakeWorkAreaRepository(), User(GrottoWorksRoles.Admin));

        var action = () => handler.HandleAsync(Command(communityId: CommunitySouth));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Dates_must_be_in_season_and_deadline_must_follow_start()
    {
        var handler = Handler(new FakeWorkAreaRepository(), User(GrottoWorksRoles.Admin));

        var invalidOrder = () => handler.HandleAsync(Command(
            startDate: new DateOnly(2026, 12, 1),
            deadline: new DateOnly(2026, 11, 1)));
        var orderException = await invalidOrder.Should().ThrowAsync<DomainException>();
        orderException.Which.Code.Should().Be("WORK_AREA_INVALID_DATE_RANGE");
        orderException.Which.StatusCode.Should().Be(422);

        var outsideSeason = () => handler.HandleAsync(Command(
            startDate: new DateOnly(2026, 9, 30),
            deadline: new DateOnly(2026, 12, 1)));
        var seasonException = await outsideSeason.Should().ThrowAsync<DomainException>();
        seasonException.Which.Code.Should().Be("WORK_AREA_DATE_OUT_OF_SEASON");
        seasonException.Which.StatusCode.Should().Be(422);
    }

    private static CreateWorkAreaCommandHandler Handler(
        FakeWorkAreaRepository workAreas,
        FakeCurrentUser user) =>
        new(Seasons(), Communities(), workAreas, user);

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId
        };

    private static CreateWorkAreaCommand Command(
        Guid? seasonId = null,
        Guid? communityId = null,
        DateOnly? startDate = null,
        DateOnly? deadline = null) =>
        new(
            seasonId ?? SeasonNorth,
            "Work Area",
            null,
            Guid.NewGuid(),
            communityId ?? CommunityNorth,
            startDate ?? new DateOnly(2026, 10, 1),
            deadline ?? new DateOnly(2026, 12, 1));

    private static FakeSeasonRepository Seasons(SeasonStatus status = SeasonStatus.Draft)
    {
        var repository = new FakeSeasonRepository();
        repository.Items.Add(Season.Create(
            SeasonNorth,
            ParishNorth,
            Guid.NewGuid(),
            "North Season",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            status,
            null,
            null,
            DateTimeOffset.UtcNow));
        return repository;
    }

    private static FakeCommunityRepository Communities()
    {
        var repository = new FakeCommunityRepository();
        repository.Items.Add(Community.Create(
            CommunityNorth,
            ParishNorth,
            "North Community",
            null,
            DateTimeOffset.UtcNow));
        repository.Items.Add(Community.Create(
            CommunitySouth,
            ParishSouth,
            "South Community",
            null,
            DateTimeOffset.UtcNow));
        return repository;
    }
}
