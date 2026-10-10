using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Application.Seasons.Queries.GetSeason;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class GetSeasonHandlerTests
{
    private static readonly Guid SeasonId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherSeasonId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ParishNorth = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Admin_can_get_a_season_from_any_parish()
    {
        var handler = new GetSeasonQueryHandler(
            RepositoryWithSeason(ParishSouth),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new GetSeasonQuery(SeasonId));

        result.Should().NotBeNull();
        result!.Id.Should().Be(SeasonId);
        result.ParishId.Should().Be(ParishSouth);
    }

    [Fact]
    public async Task Parish_user_can_get_a_season_in_their_jwt_parish()
    {
        var handler = new GetSeasonQueryHandler(
            RepositoryWithSeason(ParishNorth),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new GetSeasonQuery(SeasonId));

        result.Should().NotBeNull();
        result!.ParishId.Should().Be(ParishNorth);
    }

    [Fact]
    public async Task Parish_user_cannot_get_a_season_from_another_parish()
    {
        var handler = new GetSeasonQueryHandler(
            RepositoryWithSeason(ParishSouth),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var action = () => handler.HandleAsync(new GetSeasonQuery(SeasonId));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Anonymous_user_is_rejected()
    {
        var handler = new GetSeasonQueryHandler(
            RepositoryWithSeason(ParishNorth),
            new FakeCurrentUser { IsAuthenticated = false });

        var action = () => handler.HandleAsync(new GetSeasonQuery(SeasonId));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("UNAUTHENTICATED");
        exception.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Missing_season_returns_null_for_endpoint_not_found_mapping()
    {
        var handler = new GetSeasonQueryHandler(
            RepositoryWithSeason(ParishNorth),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new GetSeasonQuery(Guid.NewGuid()));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Detail_maps_all_public_dto_fields_without_creator_id()
    {
        var createdAt = new DateTimeOffset(2026, 8, 20, 10, 30, 0, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 8, 21, 11, 45, 0, TimeSpan.Zero);
        var repository = new FakeSeasonRepository();
        repository.Items.Add(Season.Create(
            SeasonId,
            ParishNorth,
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "  Demo Season  ",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            SeasonStatus.Active,
            "Description",
            12345.67m,
            createdAt));

        var season = repository.Items[0];
        var handler = new GetSeasonQueryHandler(
            repository,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new GetSeasonQuery(SeasonId));

        result.Should().BeEquivalentTo(new
        {
            Id = SeasonId,
            ParishId = ParishNorth,
            Name = "  Demo Season  ",
            SeasonYear = 2026,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2027, 1, 15),
            Status = SeasonStatus.Active,
            Description = "Description",
            EstimatedBudget = 12345.67m,
            CreatedAt = createdAt,
            UpdatedAt = (DateTimeOffset?)null
        });
        result.Should().NotBeNull();
        typeof(ParishCoordination.Application.Seasons.SeasonDto)
            .GetProperty(nameof(Season.CreatedByUserId))
            .Should().BeNull();
        season.UpdatedAt.Should().BeNull();
        updatedAt.Should().NotBe(default);
    }

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId
        };

    private static FakeSeasonRepository RepositoryWithSeason(Guid parishId)
    {
        var repository = new FakeSeasonRepository();
        repository.Items.Add(Season.Create(
            SeasonId,
            parishId,
            Guid.NewGuid(),
            "Demo Season",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            SeasonStatus.Draft,
            "Description",
            100m,
            DateTimeOffset.UtcNow));
        repository.Items.Add(Season.Create(
            OtherSeasonId,
            ParishNorth,
            Guid.NewGuid(),
            "Other Season",
            2025,
            new DateOnly(2025, 10, 1),
            new DateOnly(2026, 1, 15),
            SeasonStatus.Completed,
            null,
            null,
            DateTimeOffset.UtcNow));
        return repository;
    }
}