using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.Seasons.Commands.UpdateSeason;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.UnitTests;

public sealed class UpdateSeasonHandlerTests
{
    private static readonly Guid ParishNorth =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SeasonId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Parish_user_updates_planning_season_in_its_parish()
    {
        var repository = Repository(BuildSeason());
        var handler = Handler(repository, GrottoWorksRoles.Parish, ParishNorth);

        var result = await handler.HandleAsync(new UpdateSeasonCommand(
            SeasonId,
            "  Updated season  ",
            2028,
            new DateOnly(2028, 9, 1),
            new DateOnly(2029, 1, 20),
            true,
            "Updated description",
            250m,
            1));

        result.Outcome.Should().Be(UpdateSeasonOutcome.Success);
        result.Season!.Name.Should().Be("Updated season");
        result.Season.SeasonYear.Should().Be(2028);
        result.Season.StartDate.Should().Be(new DateOnly(2028, 9, 1));
        result.Season.EndDate.Should().Be(new DateOnly(2029, 1, 20));
        result.Season.Description.Should().Be("Updated description");
        result.Season.EstimatedBudget.Should().Be(250m);
        result.Season.UpdatedAt.Should().NotBeNull();
        result.Season.Version.Should().Be(2);
        repository.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Partial_update_preserves_omitted_fields_and_can_clear_description()
    {
        var repository = Repository(BuildSeason());
        var handler = Handler(repository, GrottoWorksRoles.Parish, ParishNorth);

        var result = await handler.HandleAsync(new UpdateSeasonCommand(
            SeasonId,
            null,
            null,
            null,
            null,
            true,
            null,
            null,
            null));

        result.Season!.Name.Should().Be("Original season");
        result.Season.SeasonYear.Should().Be(2027);
        result.Season.StartDate.Should().Be(new DateOnly(2027, 10, 1));
        result.Season.EndDate.Should().Be(new DateOnly(2028, 1, 15));
        result.Season.Description.Should().BeNull();
        result.Season.EstimatedBudget.Should().Be(100m);
    }

    [Fact]
    public async Task Admin_can_update_season_in_any_parish()
    {
        var repository = Repository(BuildSeason(parishId: ParishSouth));
        var handler = Handler(repository, GrottoWorksRoles.Admin, null);

        var result = await handler.HandleAsync(Command(name: "Admin update"));

        result.Outcome.Should().Be(UpdateSeasonOutcome.Success);
        result.Season!.ParishId.Should().Be(ParishSouth);
    }

    [Fact]
    public async Task User_from_another_parish_is_rejected()
    {
        var handler = Handler(
            Repository(BuildSeason(parishId: ParishSouth)),
            GrottoWorksRoles.Parish,
            ParishNorth);

        var action = () => handler.HandleAsync(Command());

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Anonymous_user_is_rejected()
    {
        var repository = Repository(BuildSeason());
        var handler = new UpdateSeasonCommandHandler(
            repository,
            new FakeCurrentUser { IsAuthenticated = false });

        var action = () => handler.HandleAsync(Command());

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("UNAUTHENTICATED");
        exception.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Unknown_season_returns_not_found()
    {
        var result = await Handler(
                new FakeSeasonRepository(),
                GrottoWorksRoles.Parish,
                ParishNorth)
            .HandleAsync(Command());

        result.Outcome.Should().Be(UpdateSeasonOutcome.NotFound);
        result.Season.Should().BeNull();
    }

    [Theory]
    [InlineData(SeasonStatus.Active)]
    [InlineData(SeasonStatus.Ready)]
    [InlineData(SeasonStatus.Completed)]
    [InlineData(SeasonStatus.Archived)]
    public async Task Only_planning_season_can_be_updated(SeasonStatus status)
    {
        var repository = Repository(BuildSeason(status: status));

        var result = await Handler(
                repository,
                GrottoWorksRoles.Parish,
                ParishNorth)
            .HandleAsync(Command());

        result.Outcome.Should().Be(UpdateSeasonOutcome.InvalidState);
        repository.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Effective_date_range_must_remain_valid_for_partial_update()
    {
        var repository = Repository(BuildSeason());

        var result = await Handler(
                repository,
                GrottoWorksRoles.Parish,
                ParishNorth)
            .HandleAsync(Command(startDate: new DateOnly(2028, 1, 15)));

        result.Outcome.Should().Be(UpdateSeasonOutcome.InvalidDateRange);
        repository.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_name_and_year_return_separate_conflicts()
    {
        var repository = Repository(
            BuildSeason(),
            BuildSeason(
                id: Guid.NewGuid(),
                name: "Existing season",
                year: 2028));
        var handler = Handler(repository, GrottoWorksRoles.Parish, ParishNorth);

        var nameResult = await handler.HandleAsync(Command(name: "Existing season"));
        var yearResult = await handler.HandleAsync(Command(year: 2028));

        nameResult.Outcome.Should().Be(UpdateSeasonOutcome.NameConflict);
        yearResult.Outcome.Should().Be(UpdateSeasonOutcome.YearConflict);
        repository.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Unique_and_concurrency_races_are_mapped_to_outcomes()
    {
        var nameRepository = Repository(BuildSeason());
        nameRepository.ThrowNameConflictOnSave = true;
        var nameResult = await Handler(nameRepository, GrottoWorksRoles.Parish, ParishNorth)
            .HandleAsync(Command(name: "Name race"));

        var yearRepository = Repository(BuildSeason());
        yearRepository.ThrowYearConflictOnSave = true;
        var yearResult = await Handler(yearRepository, GrottoWorksRoles.Parish, ParishNorth)
            .HandleAsync(Command(year: 2028));

        var concurrencyRepository = Repository(BuildSeason());
        concurrencyRepository.ThrowConcurrencyOnSave = true;
        var concurrencyResult = await Handler(
                concurrencyRepository,
                GrottoWorksRoles.Parish,
                ParishNorth)
            .HandleAsync(Command());

        nameResult.Outcome.Should().Be(UpdateSeasonOutcome.NameConflict);
        yearResult.Outcome.Should().Be(UpdateSeasonOutcome.YearConflict);
        concurrencyResult.Outcome.Should().Be(UpdateSeasonOutcome.VersionMismatch);
    }

    [Fact]
    public async Task Stale_expected_version_is_rejected_before_update()
    {
        var repository = Repository(BuildSeason());

        var result = await Handler(
                repository,
                GrottoWorksRoles.Parish,
                ParishNorth)
            .HandleAsync(Command(expectedVersion: 2));

        result.Outcome.Should().Be(UpdateSeasonOutcome.VersionMismatch);
        result.Season.Should().BeNull();
        repository.Items.Single().Name.Should().Be("Original season");
        repository.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public void Request_validates_empty_and_invalid_values()
    {
        Validate(new UpdateSeasonRequest()).Should().Contain(result =>
            result.ErrorMessage!.Contains("At least one field"));
        Validate(new UpdateSeasonRequest { Name = "   " }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateSeasonRequest.Name)));
        Validate(new UpdateSeasonRequest { Name = new string('n', 151) }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateSeasonRequest.Name)));
        Validate(new UpdateSeasonRequest { SeasonYear = 0 }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateSeasonRequest.SeasonYear)));
        Validate(new UpdateSeasonRequest { EstimatedBudget = -1m }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateSeasonRequest.EstimatedBudget)));
        Validate(new UpdateSeasonRequest
        {
            StartDate = new DateOnly(2027, 10, 1),
            EndDate = new DateOnly(2027, 10, 1)
        }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateSeasonRequest.EndDate)));
    }

    [Fact]
    public void Ef_model_maps_version_as_concurrency_token()
    {
        var options = new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new ParishCoordinationDbContext(options);
        var property = context.Model
            .FindEntityType(typeof(Season))!
            .FindProperty(nameof(Season.Version))!;

        property.GetColumnName().Should().Be("version");
        property.IsConcurrencyToken.Should().BeTrue();
        property.GetDefaultValue().Should().Be(1);
    }

    private static UpdateSeasonCommand Command(
        string? name = "Updated season",
        int? year = null,
        DateOnly? startDate = null,
        int? expectedVersion = null) =>
        new(
            SeasonId,
            name,
            year,
            startDate,
            null,
            false,
            null,
            null,
            expectedVersion);

    private static UpdateSeasonCommandHandler Handler(
        FakeSeasonRepository repository,
        string role,
        Guid? parishId) =>
        new(
            repository,
            new FakeCurrentUser
            {
                IsAuthenticated = true,
                Role = role,
                ParishId = parishId
            });

    private static FakeSeasonRepository Repository(params Season[] seasons)
    {
        var repository = new FakeSeasonRepository();
        repository.Items.AddRange(seasons);
        return repository;
    }

    private static Season BuildSeason(
        Guid? id = null,
        Guid? parishId = null,
        string name = "Original season",
        int year = 2027,
        SeasonStatus status = SeasonStatus.Planning) =>
        ParishCoordination.Domain.Entities.Season.Create(
            id ?? SeasonId,
            parishId ?? ParishNorth,
            Guid.NewGuid(),
            name,
            year,
            new DateOnly(year, 10, 1),
            new DateOnly(year + 1, 1, 15),
            status,
            "Original description",
            100m,
            DateTimeOffset.UtcNow);

    private static List<ValidationResult> Validate(UpdateSeasonRequest request)
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
