using BuildingBlocks.Security;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ParishCoordination.Api.Endpoints;
using ParishCoordination.Application.Communities.Queries.GetCommunities;
using ParishCoordination.Application.Parishes.Commands.CreateParish;
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
        builder.Services.AddScoped<IReadinessProbe, TestReadinessProbe>();
        using var app = builder.Build();

        app.MapParishEndpoints();
        app.MapCommunityEndpoints();
        app.MapGrottoWorksHealthEndpoints();
        await app.StartAsync();

        var listParishes = FindByName(app, "listParishes");
        var createParish = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "createParish");
        var communities = FindByName(app, "listCommunities");
        var live = FindByName(app, "livenessProbe");

        GetRoles(listParishes).Should().BeEquivalentTo(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish);
        GetRoles(createParish).Should().BeEquivalentTo(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish);
        (communities.Metadata.GetMetadata<IAuthorizeData>() is not null ||
            communities.Metadata.GetMetadata<AuthorizationPolicy>() is not null).Should().BeTrue();
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