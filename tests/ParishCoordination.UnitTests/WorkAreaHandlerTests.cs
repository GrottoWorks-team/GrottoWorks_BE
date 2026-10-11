using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Application.WorkAreas.Queries.GetWorkArea;
using ParishCoordination.Application.WorkAreas.Queries.ListWorkAreas;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.UnitTests;

public sealed class WorkAreaHandlerTests
{
    private static readonly Guid ParishNorth = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SeasonNorth = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SeasonSouth = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    [Fact]
    public async Task Current_parish_user_can_list_work_areas_for_its_season()
    {
        var seasons = Seasons();
        var repository = WorkAreas();
        var handler = new ListWorkAreasQueryHandler(
            seasons,
            repository,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new ListWorkAreasQuery(SeasonNorth, 1, 20, "name,asc"));

        result!.TotalItems.Should().Be(2);
        result.Items.Should().OnlyContain(workArea => workArea.SeasonId == SeasonNorth);
    }

    [Fact]
    public async Task Admin_can_list_work_areas_for_any_parish()
    {
        var handler = new ListWorkAreasQueryHandler(
            Seasons(),
            WorkAreas(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new ListWorkAreasQuery(SeasonSouth, 1, 20, null));

        result!.Items.Should().ContainSingle();
        result.Items[0].CommunityId.Should().BeNull();
    }

    [Fact]
    public async Task User_from_another_parish_is_rejected_for_list()
    {
        var handler = new ListWorkAreasQueryHandler(
            Seasons(),
            WorkAreas(),
            User(GrottoWorksRoles.Parish, ParishSouth));

        var action = () => handler.HandleAsync(new ListWorkAreasQuery(SeasonNorth, 1, 20, null));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Anonymous_user_is_rejected_for_list()
    {
        var handler = new ListWorkAreasQueryHandler(
            Seasons(),
            WorkAreas(),
            new FakeCurrentUser { IsAuthenticated = false });

        var action = () => handler.HandleAsync(new ListWorkAreasQuery(SeasonNorth, 1, 20, null));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("UNAUTHENTICATED");
        exception.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Missing_season_returns_null_for_endpoint_to_map_not_found()
    {
        var handler = new ListWorkAreasQueryHandler(
            Seasons(),
            WorkAreas(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new ListWorkAreasQuery(Guid.NewGuid(), 1, 20, null));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Empty_list_preserves_zero_total()
    {
        var handler = new ListWorkAreasQueryHandler(
            Seasons(),
            new FakeWorkAreaRepository(),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new ListWorkAreasQuery(SeasonNorth, 1, 20, null));

        result!.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task List_supports_pagination_sort_and_stable_id_tie_break()
    {
        var repository = WorkAreas();
        var handler = new ListWorkAreasQueryHandler(
            Seasons(),
            repository,
            User(GrottoWorksRoles.Admin));

        var page = await handler.HandleAsync(new ListWorkAreasQuery(SeasonNorth, 2, 1, "name,asc"));
        var statusSorted = await handler.HandleAsync(new ListWorkAreasQuery(SeasonNorth, 1, 20, "status,desc"));
        var idSorted = await handler.HandleAsync(new ListWorkAreasQuery(SeasonNorth, 1, 20, "id,asc"));

        page!.TotalItems.Should().Be(2);
        page.Items.Should().ContainSingle();
        page.Items[0].Id.Should().Be(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"));
        statusSorted!.Items.Select(item => item.Status).Should().BeInDescendingOrder();
        idSorted!.Items.Select(item => item.Id).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Admin_and_same_parish_user_can_get_work_area_detail()
    {
        var workAreaId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

        var adminHandler = new GetWorkAreaQueryHandler(
            WorkAreas(),
            Seasons(),
            User(GrottoWorksRoles.Admin));
        var parishHandler = new GetWorkAreaQueryHandler(
            WorkAreas(),
            Seasons(),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var adminResult = await adminHandler.HandleAsync(new GetWorkAreaQuery(workAreaId));
        var parishResult = await parishHandler.HandleAsync(new GetWorkAreaQuery(workAreaId));

        adminResult.Should().BeEquivalentTo(parishResult);
        adminResult!.Id.Should().Be(workAreaId);
    }

    [Fact]
    public async Task Other_parish_is_rejected_for_detail()
    {
        var handler = new GetWorkAreaQueryHandler(
            WorkAreas(),
            Seasons(),
            User(GrottoWorksRoles.Parish, ParishSouth));

        var action = () => handler.HandleAsync(new GetWorkAreaQuery(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Anonymous_user_is_rejected_for_detail()
    {
        var handler = new GetWorkAreaQueryHandler(
            WorkAreas(),
            Seasons(),
            new FakeCurrentUser { IsAuthenticated = false });

        var action = () => handler.HandleAsync(new GetWorkAreaQuery(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("UNAUTHENTICATED");
        exception.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Missing_work_area_returns_null()
    {
        var handler = new GetWorkAreaQueryHandler(
            WorkAreas(),
            Seasons(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new GetWorkAreaQuery(Guid.NewGuid()));

        result.Should().BeNull();
    }

    [Fact]
    public void Dto_exposes_only_the_contract_fields()
    {
        var dtoProperties = typeof(WorkAreaDto).GetProperties().Select(property => property.Name);

        dtoProperties.Should().BeEquivalentTo(
            nameof(WorkAreaDto.Id),
            nameof(WorkAreaDto.SeasonId),
            nameof(WorkAreaDto.CommunityId),
            nameof(WorkAreaDto.LeaderId),
            nameof(WorkAreaDto.MaterialOfficerId),
            nameof(WorkAreaDto.Status));
        dtoProperties.Should().NotContain("Name");
        dtoProperties.Should().NotContain("Description");
        dtoProperties.Should().NotContain("StartDate");
        dtoProperties.Should().NotContain("Deadline");
        dtoProperties.Should().NotContain("WorkAreaCategoryId");
    }

    [Fact]
    public async Task Seeder_is_idempotent_and_work_area_model_matches_schema()
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

        context.WorkAreas.Should().ContainSingle();
        context.WorkAreas.Single().Id.Should().Be(ParishCoordinationDataSeeder.DemoWorkAreaId);

        var entity = context.Model.FindEntityType(typeof(WorkArea))!;
        entity.GetTableName().Should().Be("work_area");
        entity.FindProperty(nameof(WorkArea.StartDate))!.GetColumnName().Should().Be("start_date");
        entity.FindProperty(nameof(WorkArea.Deadline))!.GetColumnName().Should().Be("deadline");
        entity.FindProperty(nameof(WorkArea.Status))!.GetColumnName().Should().Be("work_area_status");
        entity.FindProperty(nameof(WorkArea.Status))!.GetValueConverter().Should().NotBeNull();
        entity.GetForeignKeys().Should().Contain(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Season) &&
            foreignKey.Properties.Single().Name == nameof(WorkArea.SeasonId));
        entity.GetForeignKeys().Should().Contain(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Community) &&
            foreignKey.Properties.Single().Name == nameof(WorkArea.CommunityId));
    }

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId
        };

    private static FakeSeasonRepository Seasons()
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
            SeasonStatus.Draft,
            null,
            null,
            DateTimeOffset.UtcNow));
        repository.Items.Add(Season.Create(
            SeasonSouth,
            ParishSouth,
            Guid.NewGuid(),
            "South Season",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            SeasonStatus.Draft,
            null,
            null,
            DateTimeOffset.UtcNow));
        return repository;
    }

    private static FakeWorkAreaRepository WorkAreas()
    {
        var repository = new FakeWorkAreaRepository();
        repository.Items.Add(WorkArea.Create(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            SeasonNorth,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            "Alpha Area",
            "description",
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 12, 1),
            WorkAreaStatus.Planned,
            DateTimeOffset.UtcNow));
        repository.Items.Add(WorkArea.Create(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            SeasonNorth,
            null,
            null,
            null,
            Guid.NewGuid(),
            "Beta Area",
            null,
            null,
            null,
            WorkAreaStatus.Completed,
            DateTimeOffset.UtcNow));
        repository.Items.Add(WorkArea.Create(
            Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            SeasonSouth,
            null,
            null,
            null,
            null,
            "South Area",
            null,
            null,
            null,
            WorkAreaStatus.InProgress,
            DateTimeOffset.UtcNow));
        return repository;
    }
}
