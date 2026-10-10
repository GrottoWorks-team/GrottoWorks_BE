using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Application.Seasons.Commands.CreateSeason;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class CreateSeasonHandlerTests
{
    private static readonly Guid ParishNorth = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid UserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Parish_user_creates_in_its_jwt_parish_with_defaults_and_audit_fields()
    {
        var parishRepository = Parishes(ParishNorth);
        var seasonRepository = new FakeSeasonRepository();
        var handler = new CreateSeasonCommandHandler(
            parishRepository,
            seasonRepository,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(Command(name: "  Christmas  "));

        result.Outcome.Should().Be(CreateSeasonOutcome.Success);
        result.Season.Should().NotBeNull();
        result.Season!.Name.Should().Be("Christmas");
        result.Season.Status.Should().Be(SeasonStatus.Draft);
        result.Season.ParishId.Should().Be(ParishNorth);
        result.Season.Id.Should().NotBeEmpty();
        seasonRepository.Items.Should().ContainSingle();
        seasonRepository.Items[0].CreatedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Admin_can_create_for_any_parish_selected_in_body()
    {
        var handler = new CreateSeasonCommandHandler(
            Parishes(ParishNorth, ParishSouth),
            new FakeSeasonRepository(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(Command(ParishSouth, "South season"));

        result.Outcome.Should().Be(CreateSeasonOutcome.Success);
        result.Season!.ParishId.Should().Be(ParishSouth);
    }

    [Fact]
    public async Task Admin_without_parish_id_is_rejected()
    {
        var handler = new CreateSeasonCommandHandler(
            Parishes(ParishNorth),
            new FakeSeasonRepository(),
            User(GrottoWorksRoles.Admin));

        var action = () => handler.HandleAsync(Command());

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ID_REQUIRED");
        exception.Which.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Parish_user_cannot_override_tenant_with_another_parish()
    {
        var handler = new CreateSeasonCommandHandler(
            Parishes(ParishNorth, ParishSouth),
            new FakeSeasonRepository(),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var action = () => handler.HandleAsync(Command(ParishSouth));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Anonymous_user_is_rejected()
    {
        var handler = new CreateSeasonCommandHandler(
            Parishes(ParishNorth),
            new FakeSeasonRepository(),
            new FakeCurrentUser { IsAuthenticated = false });

        var action = () => handler.HandleAsync(Command(ParishNorth));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("UNAUTHENTICATED");
        exception.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Missing_parish_returns_parish_not_found()
    {
        var missingParish = Guid.NewGuid();
        var handler = new CreateSeasonCommandHandler(
            Parishes(ParishNorth),
            new FakeSeasonRepository(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(Command(missingParish));

        result.Outcome.Should().Be(CreateSeasonOutcome.ParishNotFound);
    }

    [Fact]
    public async Task Duplicate_name_and_year_return_separate_conflicts()
    {
        var repository = new FakeSeasonRepository();
        repository.Items.Add(Season.Create(
            ParishNorth,
            UserId,
            "Existing",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            null,
            null,
            DateTimeOffset.UtcNow));
        var user = User(GrottoWorksRoles.Parish, ParishNorth);

        var nameHandler = new CreateSeasonCommandHandler(Parishes(ParishNorth), repository, user);
        var nameResult = await nameHandler.HandleAsync(Command(name: " Existing ", year: 2027));
        nameResult.Outcome.Should().Be(CreateSeasonOutcome.NameConflict);

        var yearHandler = new CreateSeasonCommandHandler(Parishes(ParishNorth), repository, user);
        var yearResult = await yearHandler.HandleAsync(Command(name: "New", year: 2026));
        yearResult.Outcome.Should().Be(CreateSeasonOutcome.YearConflict);
    }

    [Fact]
    public async Task Same_name_and_year_in_another_parish_is_allowed()
    {
        var repository = new FakeSeasonRepository();
        repository.Items.Add(Season.Create(
            ParishNorth,
            UserId,
            "Same",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            null,
            null,
            DateTimeOffset.UtcNow));
        var handler = new CreateSeasonCommandHandler(
            Parishes(ParishNorth, ParishSouth),
            repository,
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(Command(ParishSouth, "Same", 2026));

        result.Outcome.Should().Be(CreateSeasonOutcome.Success);
        result.Season!.ParishId.Should().Be(ParishSouth);
    }

    [Fact]
    public async Task Unique_index_races_are_mapped_to_conflicts()
    {
        var nameRepository = new FakeSeasonRepository { ThrowNameConflictOnSave = true };
        var nameHandler = new CreateSeasonCommandHandler(
            Parishes(ParishNorth),
            nameRepository,
            User(GrottoWorksRoles.Parish, ParishNorth));

        (await nameHandler.HandleAsync(Command(name: "Race")))
            .Outcome.Should().Be(CreateSeasonOutcome.NameConflict);

        var yearRepository = new FakeSeasonRepository { ThrowYearConflictOnSave = true };
        var yearHandler = new CreateSeasonCommandHandler(
            Parishes(ParishNorth),
            yearRepository,
            User(GrottoWorksRoles.Parish, ParishNorth));

        (await yearHandler.HandleAsync(Command(name: "Race 2")))
            .Outcome.Should().Be(CreateSeasonOutcome.YearConflict);
    }

    private static CreateSeasonCommand Command(
        Guid? parishId = null,
        string name = "Demo Season",
        int year = 2027) =>
        new(
            parishId,
            name,
            year,
            new DateOnly(year, 10, 1),
            new DateOnly(year + 1, 1, 15),
            " description ",
            100m);

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            UserId = UserId,
            Role = role,
            ParishId = parishId
        };

    private static FakeParishRepository Parishes(params Guid[] ids)
    {
        var repository = new FakeParishRepository();
        foreach (var id in ids)
        {
            repository.Items.Add(Parish.Create(id, $"Parish {id}", null, null, DateTimeOffset.UtcNow));
        }

        return repository;
    }
}