using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParishCoordination.Application.Seasons.Queries.GetSeasons;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.UnitTests;

public sealed class SeasonHandlerTests
{
    private static readonly Guid ParishNorth = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Admin_can_list_all_seasons_with_latest_year_first()
    {
        var repository = RepositoryWithSeasons();
        var handler = new GetSeasonsQueryHandler(
            repository,
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new GetSeasonsQuery());

        result.TotalItems.Should().Be(3);
        result.Items.Select(season => season.SeasonYear)
            .Should().Equal(2026, 2025, 2024);
        repository.LastScopeId.Should().BeNull();
    }

    [Fact]
    public async Task Non_admin_user_can_list_only_current_parish_seasons()
    {
        var repository = RepositoryWithSeasons();
        var handler = new GetSeasonsQueryHandler(
            repository,
            User(GrottoWorksRoles.Volunteer, ParishNorth));

        var result = await handler.HandleAsync(new GetSeasonsQuery());

        result.TotalItems.Should().Be(2);
        result.Items.Should().OnlyContain(season => season.ParishId == ParishNorth);
        repository.LastScopeId.Should().Be(ParishNorth);
    }

    [Fact]
    public async Task Anonymous_user_is_rejected()
    {
        var handler = new GetSeasonsQueryHandler(
            RepositoryWithSeasons(),
            new FakeCurrentUser { IsAuthenticated = false });

        var action = () => handler.HandleAsync(new GetSeasonsQuery());

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("UNAUTHENTICATED");
        exception.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Authenticated_user_without_parish_is_rejected()
    {
        var handler = new GetSeasonsQueryHandler(
            RepositoryWithSeasons(),
            User(GrottoWorksRoles.Volunteer));

        var action = () => handler.HandleAsync(new GetSeasonsQuery());

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Empty_result_keeps_pagination_total_zero()
    {
        var handler = new GetSeasonsQueryHandler(
            new FakeSeasonRepository(),
            User(GrottoWorksRoles.Volunteer, ParishNorth));

        var result = await handler.HandleAsync(new GetSeasonsQuery(1, 20));

        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Pagination_returns_requested_page_and_size()
    {
        var handler = new GetSeasonsQueryHandler(
            RepositoryWithSeasons(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new GetSeasonsQuery(2, 1));

        result.TotalItems.Should().Be(3);
        result.Items.Should().ContainSingle();
        result.Items[0].SeasonYear.Should().Be(2025);
    }

    [Theory]
    [InlineData("name,asc")]
    [InlineData("seasonYear,asc")]
    [InlineData("startDate,asc")]
    [InlineData("endDate,asc")]
    [InlineData("status,asc")]
    public async Task Supported_sort_fields_are_accepted(string sort)
    {
        var handler = new GetSeasonsQueryHandler(
            RepositoryWithSeasons(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new GetSeasonsQuery(1, 20, sort));

        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Seeder_is_idempotent_and_model_has_season_indexes()
    {
        var options = new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        await using var context = new ParishCoordinationDbContext(options);
        var seeder = new ParishCoordinationDataSeeder(
            context,
            loggerFactory.CreateLogger<ParishCoordinationDataSeeder>());

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        context.Seasons.Should().ContainSingle();
        context.Seasons.Single().Id.Should().Be(ParishCoordinationDataSeeder.DemoSeasonId);

        var entity = context.Model.FindEntityType(typeof(Season))!;
        entity.GetTableName().Should().Be("christmas_season");
        entity.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Season.ParishId), nameof(Season.SeasonYear) }));
        entity.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Season.ParishId), nameof(Season.Name) }));
        entity.FindProperty(nameof(Season.EstimatedBudget))!.GetPrecision().Should().Be(14);
        entity.FindProperty(nameof(Season.EstimatedBudget))!.GetScale().Should().Be(2);
    }

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId
        };

    private static FakeSeasonRepository RepositoryWithSeasons()
    {
        var repository = new FakeSeasonRepository();
        repository.Items.Add(Season.Create(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            ParishNorth,
            Guid.NewGuid(),
            "Season 2024",
            2024,
            new DateOnly(2024, 10, 1),
            new DateOnly(2025, 1, 15),
            SeasonStatus.Completed,
            null,
            100m,
            DateTimeOffset.UtcNow));
        repository.Items.Add(Season.Create(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            ParishNorth,
            Guid.NewGuid(),
            "Season 2026",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            SeasonStatus.Draft,
            null,
            200m,
            DateTimeOffset.UtcNow));
        repository.Items.Add(Season.Create(
            Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            ParishSouth,
            Guid.NewGuid(),
            "Season 2025",
            2025,
            new DateOnly(2025, 10, 1),
            new DateOnly(2026, 1, 15),
            SeasonStatus.Active,
            null,
            null,
            DateTimeOffset.UtcNow));

        return repository;
    }
}

