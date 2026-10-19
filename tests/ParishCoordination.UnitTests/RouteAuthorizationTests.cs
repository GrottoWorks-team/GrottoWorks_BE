using BuildingBlocks.Security;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ParishCoordination.Api.Endpoints;
using ParishCoordination.Application.Communities.Commands.CreateCommunity;
using ParishCoordination.Application.Communities.Commands.UpdateCommunity;
using ParishCoordination.Application.Communities.Queries.GetCommunities;
using ParishCoordination.Application.Parishes.Commands.CreateParish;
using ParishCoordination.Application.Seasons.Queries.GetSeasons;
using ParishCoordination.Application.Seasons.Queries.GetSeason;
using ParishCoordination.Application.Seasons.Commands.CreateSeason;
using ParishCoordination.Application.Seasons.Commands.UpdateSeason;
using ParishCoordination.Application.WorkAreas.Queries.GetWorkArea;
using ParishCoordination.Application.WorkAreas.Queries.ListWorkAreas;
using ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaCommunity;
using ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaLeader;
using ParishCoordination.Application.WorkAreas.Commands.AssignMaterialOfficer;
using ParishCoordination.Application.WorkAreas.Commands.CreateWorkArea;
using ParishCoordination.Application.WorkAreas.Commands.UpdateWorkArea;
using ParishCoordination.Application.WorkAreaCategories.Queries.ListWorkAreaCategories;
using ParishCoordination.Application.WorkAreaCategories.Commands.CreateWorkAreaCategory;
using ParishCoordination.Application.WorkAreaCategories.Commands.UpdateWorkAreaCategory;
using ParishCoordination.Application.Parishes.Commands.UpdateParish;
using ParishCoordination.Application.Parishes.Queries.GetParish;
using ParishCoordination.Application.Parishes.Queries.GetParishes;

namespace ParishCoordination.UnitTests;

public sealed class RouteAuthorizationTests
{
    [Fact]
    public async Task Existing_business_routes_require_expected_authorization_and_health_is_anonymous()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.Services.AddRouting();
        builder.Services.AddValidation();
        builder.Services.AddScoped<GetParishesQueryHandler>();
        builder.Services.AddScoped<GetParishQueryHandler>();
        builder.Services.AddScoped<UpdateParishCommandHandler>();
        builder.Services.AddScoped<CreateParishCommandHandler>();
        builder.Services.AddScoped<GetCommunitiesQueryHandler>();
        builder.Services.AddScoped<CreateCommunityCommandHandler>();
        builder.Services.AddScoped<UpdateCommunityCommandHandler>();
        builder.Services.AddScoped<GetSeasonsQueryHandler>();
        builder.Services.AddScoped<GetSeasonQueryHandler>();
        builder.Services.AddScoped<CreateSeasonCommandHandler>();
        builder.Services.AddScoped<UpdateSeasonCommandHandler>();
        builder.Services.AddScoped<ListWorkAreasQueryHandler>();
        builder.Services.AddScoped<GetWorkAreaQueryHandler>();
        builder.Services.AddScoped<AssignWorkAreaCommunityCommandHandler>();
        builder.Services.AddScoped<AssignWorkAreaLeaderCommandHandler>();
        builder.Services.AddScoped<AssignMaterialOfficerCommandHandler>();
        builder.Services.AddScoped<CreateWorkAreaCommandHandler>();
        builder.Services.AddScoped<UpdateWorkAreaCommandHandler>();
        builder.Services.AddScoped<ListWorkAreaCategoriesQueryHandler>();
        builder.Services.AddScoped<CreateWorkAreaCategoryCommandHandler>();
        builder.Services.AddScoped<UpdateWorkAreaCategoryCommandHandler>();
        builder.Services.AddScoped<IReadinessProbe, TestReadinessProbe>();
        using var app = builder.Build();

        app.MapParishEndpoints();
        app.MapCommunityEndpoints();
        app.MapSeasonEndpoints();
        app.MapWorkAreaEndpoints();
        app.MapWorkAreaCategoryEndpoints();
        app.MapGrottoWorksHealthEndpoints();
        await app.StartAsync();

        var listParishes = FindByName(app, "listParishes");
        var createParish = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "createParish");
        var communities = FindByName(app, "listCommunities");
        var createCommunity = FindByName(app, "createCommunity");
        var updateCommunity = FindByName(app, "updateCommunity");
        var seasons = FindByName(app, "listSeasons");
        var getSeason = FindByName(app, "getSeason");
        var createSeason = FindByName(app, "createSeason");
        var updateSeason = FindByName(app, "updateSeason");
        var listWorkAreas = FindByName(app, "listWorkAreas");
        var getWorkArea = FindByName(app, "getWorkArea");
        var createWorkArea = FindByName(app, "createWorkArea");
        var assignWorkAreaCommunity = FindByName(app, "assignWorkAreaCommunity");
        var assignWorkAreaLeader = FindByName(app, "assignWorkAreaLeader");
        var assignMaterialOfficer = FindByName(app, "assignMaterialOfficer");
        var updateWorkArea = FindByName(app, "updateWorkArea");
        var listWorkAreaCategories = FindByName(app, "listWorkAreaCategories");
        var createWorkAreaCategory = FindByName(app, "createWorkAreaCategory");
        var updateWorkAreaCategory = FindByName(app, "updateWorkAreaCategory");
        var live = FindByName(app, "livenessProbe");

        GetRoles(listParishes).Should().BeEquivalentTo(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish);
        GetRoles(createParish).Should().BeEquivalentTo(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish);
        GetRoles(createCommunity).Should().BeEquivalentTo(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish);
        GetRoles(updateCommunity).Should().BeEquivalentTo(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish);
        GetRoles(createSeason).Should().BeEquivalentTo(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish);
        GetRoles(updateSeason).Should().BeEquivalentTo(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish);
        GetRoles(createWorkArea).Should().BeEquivalentTo(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish);
        GetRoles(assignWorkAreaCommunity).Should().BeEquivalentTo(
            GrottoWorksRoles.Admin,
            GrottoWorksRoles.Parish);
        GetRoles(assignWorkAreaLeader).Should().BeEquivalentTo(
            GrottoWorksRoles.Admin,
            GrottoWorksRoles.Parish);
        GetRoles(assignMaterialOfficer).Should().BeEquivalentTo(
            GrottoWorksRoles.Admin,
            GrottoWorksRoles.Parish);
        GetRoles(updateWorkArea).Should().BeEquivalentTo(
            GrottoWorksRoles.Admin,
            GrottoWorksRoles.Parish,
            GrottoWorksRoles.Leader);
        createSeason.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Should().Contain(metadata => metadata.StatusCode == 201);
        updateSeason.RoutePattern.RawText.Should().Be("/api/v1/seasons/{seasonId}");
        var updateSeasonStatusCodes = updateSeason.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();
        updateSeasonStatusCodes.Should().Contain(StatusCodes.Status200OK);
        updateSeasonStatusCodes.Should().Contain(StatusCodes.Status400BadRequest);
        updateSeasonStatusCodes.Should().Contain(StatusCodes.Status401Unauthorized);
        updateSeasonStatusCodes.Should().Contain(StatusCodes.Status403Forbidden);
        updateSeasonStatusCodes.Should().Contain(StatusCodes.Status404NotFound);
        updateSeasonStatusCodes.Should().Contain(StatusCodes.Status409Conflict);
        updateSeasonStatusCodes.Should().Contain(StatusCodes.Status412PreconditionFailed);
        updateSeasonStatusCodes.Should().Contain(StatusCodes.Status422UnprocessableEntity);
        createWorkArea.RoutePattern.RawText.Should().Be("/api/v1/seasons/{seasonId}/work-areas");
        createWorkArea.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Should().Contain(metadata => metadata.StatusCode == 201);
        createWorkArea.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Should().Contain(metadata => metadata.StatusCode == 409);
        createWorkArea.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Should().Contain(metadata => metadata.StatusCode == 422);
        assignWorkAreaCommunity.RoutePattern.RawText.Should()
            .Be("/api/v1/work-areas/{workAreaId}/community");
        var assignCommunityStatusCodes = assignWorkAreaCommunity.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();
        assignCommunityStatusCodes.Should().Contain(StatusCodes.Status200OK);
        assignCommunityStatusCodes.Should().Contain(StatusCodes.Status400BadRequest);
        assignCommunityStatusCodes.Should().Contain(StatusCodes.Status401Unauthorized);
        assignCommunityStatusCodes.Should().Contain(StatusCodes.Status403Forbidden);
        assignCommunityStatusCodes.Should().Contain(StatusCodes.Status404NotFound);
        assignCommunityStatusCodes.Should().Contain(StatusCodes.Status409Conflict);
        assignCommunityStatusCodes.Should().Contain(StatusCodes.Status412PreconditionFailed);
        assignCommunityStatusCodes.Should().Contain(StatusCodes.Status422UnprocessableEntity);
        assignWorkAreaLeader.RoutePattern.RawText.Should()
            .Be("/api/v1/work-areas/{workAreaId}/leader");
        var assignLeaderStatusCodes = assignWorkAreaLeader.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();
        assignLeaderStatusCodes.Should().Contain(StatusCodes.Status200OK);
        assignLeaderStatusCodes.Should().Contain(StatusCodes.Status400BadRequest);
        assignLeaderStatusCodes.Should().Contain(StatusCodes.Status401Unauthorized);
        assignLeaderStatusCodes.Should().Contain(StatusCodes.Status403Forbidden);
        assignLeaderStatusCodes.Should().Contain(StatusCodes.Status404NotFound);
        assignLeaderStatusCodes.Should().Contain(StatusCodes.Status409Conflict);
        assignLeaderStatusCodes.Should().Contain(StatusCodes.Status412PreconditionFailed);
        assignLeaderStatusCodes.Should().Contain(StatusCodes.Status422UnprocessableEntity);
        assignLeaderStatusCodes.Should().Contain(StatusCodes.Status503ServiceUnavailable);
        assignMaterialOfficer.RoutePattern.RawText.Should()
            .Be("/api/v1/work-areas/{workAreaId}/material-officer");
        var assignMaterialOfficerStatusCodes = assignMaterialOfficer.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();
        assignMaterialOfficerStatusCodes.Should().Contain(StatusCodes.Status200OK);
        assignMaterialOfficerStatusCodes.Should().Contain(StatusCodes.Status400BadRequest);
        assignMaterialOfficerStatusCodes.Should().Contain(StatusCodes.Status401Unauthorized);
        assignMaterialOfficerStatusCodes.Should().Contain(StatusCodes.Status403Forbidden);
        assignMaterialOfficerStatusCodes.Should().Contain(StatusCodes.Status404NotFound);
        assignMaterialOfficerStatusCodes.Should().Contain(StatusCodes.Status409Conflict);
        assignMaterialOfficerStatusCodes.Should().Contain(StatusCodes.Status412PreconditionFailed);
        assignMaterialOfficerStatusCodes.Should().Contain(StatusCodes.Status422UnprocessableEntity);
        assignMaterialOfficerStatusCodes.Should().Contain(StatusCodes.Status503ServiceUnavailable);
        updateWorkArea.RoutePattern.RawText.Should().Be("/api/v1/work-areas/{workAreaId}");
        var updateWorkAreaStatusCodes = updateWorkArea.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();
        updateWorkAreaStatusCodes.Should().Contain(StatusCodes.Status200OK);
        updateWorkAreaStatusCodes.Should().Contain(StatusCodes.Status400BadRequest);
        updateWorkAreaStatusCodes.Should().Contain(StatusCodes.Status401Unauthorized);
        updateWorkAreaStatusCodes.Should().Contain(StatusCodes.Status403Forbidden);
        updateWorkAreaStatusCodes.Should().Contain(StatusCodes.Status404NotFound);
        updateWorkAreaStatusCodes.Should().Contain(StatusCodes.Status409Conflict);
        updateWorkAreaStatusCodes.Should().Contain(StatusCodes.Status412PreconditionFailed);
        updateWorkAreaStatusCodes.Should().Contain(StatusCodes.Status422UnprocessableEntity);
        listWorkAreas.RoutePattern.RawText.Should().Be("/api/v1/seasons/{seasonId}/work-areas");
        getWorkArea.RoutePattern.RawText.Should().Be("/api/v1/work-areas/{workAreaId}");
        (listWorkAreas.Metadata.GetMetadata<IAuthorizeData>() is not null ||
            listWorkAreas.Metadata.GetMetadata<AuthorizationPolicy>() is not null).Should().BeTrue();
        (getWorkArea.Metadata.GetMetadata<IAuthorizeData>() is not null ||
            getWorkArea.Metadata.GetMetadata<AuthorizationPolicy>() is not null).Should().BeTrue();
        listWorkAreas.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Should().Contain(metadata => metadata.StatusCode == 200);
        listWorkAreas.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Should().Contain(metadata => metadata.StatusCode == 404);
        getWorkArea.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Should().Contain(metadata => metadata.StatusCode == 200);
        getWorkArea.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Should().Contain(metadata => metadata.StatusCode == 404);
        listWorkAreaCategories.RoutePattern.RawText.Should().Be("/api/v1/work-area-categories");
        (listWorkAreaCategories.Metadata.GetMetadata<IAuthorizeData>() is not null ||
            listWorkAreaCategories.Metadata.GetMetadata<AuthorizationPolicy>() is not null).Should().BeTrue();
        var categoryStatusCodes = listWorkAreaCategories.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();
        categoryStatusCodes.Should().Contain(StatusCodes.Status200OK);
        categoryStatusCodes.Should().Contain(StatusCodes.Status400BadRequest);
        categoryStatusCodes.Should().Contain(StatusCodes.Status401Unauthorized);
        createWorkAreaCategory.RoutePattern.RawText.Should().Be("/api/v1/work-area-categories");
        GetRoles(createWorkAreaCategory).Should().BeEquivalentTo(GrottoWorksRoles.Admin);
        var createCategoryStatusCodes = createWorkAreaCategory.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();
        createCategoryStatusCodes.Should().Contain(StatusCodes.Status201Created);
        createCategoryStatusCodes.Should().Contain(StatusCodes.Status400BadRequest);
        createCategoryStatusCodes.Should().Contain(StatusCodes.Status401Unauthorized);
        createCategoryStatusCodes.Should().Contain(StatusCodes.Status403Forbidden);
        createCategoryStatusCodes.Should().Contain(StatusCodes.Status409Conflict);
        createCategoryStatusCodes.Should().Contain(StatusCodes.Status422UnprocessableEntity);
        updateWorkAreaCategory.RoutePattern.RawText.Should()
            .Be("/api/v1/work-area-categories/{categoryId}");
        GetRoles(updateWorkAreaCategory).Should().BeEquivalentTo(GrottoWorksRoles.Admin);
        var updateCategoryStatusCodes = updateWorkAreaCategory.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();
        updateCategoryStatusCodes.Should().Contain(StatusCodes.Status200OK);
        updateCategoryStatusCodes.Should().Contain(StatusCodes.Status400BadRequest);
        updateCategoryStatusCodes.Should().Contain(StatusCodes.Status401Unauthorized);
        updateCategoryStatusCodes.Should().Contain(StatusCodes.Status403Forbidden);
        updateCategoryStatusCodes.Should().Contain(StatusCodes.Status404NotFound);
        updateCategoryStatusCodes.Should().Contain(StatusCodes.Status422UnprocessableEntity);
        getSeason.RoutePattern.RawText.Should().Be("/api/v1/seasons/{seasonId}");
        (getSeason.Metadata.GetMetadata<IAuthorizeData>() is not null ||
            getSeason.Metadata.GetMetadata<AuthorizationPolicy>() is not null).Should().BeTrue();
        getSeason.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Should().Contain(metadata => metadata.StatusCode == 200);
        getSeason.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Should().Contain(metadata => metadata.StatusCode == 404);
        (seasons.Metadata.GetMetadata<IAuthorizeData>() is not null ||
            seasons.Metadata.GetMetadata<AuthorizationPolicy>() is not null).Should().BeTrue();
        (communities.Metadata.GetMetadata<IAuthorizeData>() is not null ||
            communities.Metadata.GetMetadata<AuthorizationPolicy>() is not null).Should().BeTrue();
        updateCommunity.RoutePattern.RawText.Should().Be("/api/v1/communities/{communityId}");
        var updateCommunityStatusCodes = updateCommunity.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();
        updateCommunityStatusCodes.Should().Contain(StatusCodes.Status200OK);
        updateCommunityStatusCodes.Should().Contain(StatusCodes.Status400BadRequest);
        updateCommunityStatusCodes.Should().Contain(StatusCodes.Status401Unauthorized);
        updateCommunityStatusCodes.Should().Contain(StatusCodes.Status403Forbidden);
        updateCommunityStatusCodes.Should().Contain(StatusCodes.Status404NotFound);
        updateCommunityStatusCodes.Should().Contain(StatusCodes.Status409Conflict);
        updateCommunityStatusCodes.Should().Contain(StatusCodes.Status412PreconditionFailed);
        updateCommunityStatusCodes.Should().Contain(StatusCodes.Status422UnprocessableEntity);
        live.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
    }

    private static IEnumerable<string> GetRoles(RouteEndpoint endpoint)
    {
        return endpoint.Metadata.GetMetadata<AuthorizationPolicy>()!
            .Requirements.OfType<RolesAuthorizationRequirement>().Single().AllowedRoles!;
    }

    private sealed class TestReadinessProbe : IReadinessProbe
    {
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static RouteEndpoint FindByName(WebApplication app, string name)
    {
        return app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == name);
    }
}
