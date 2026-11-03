using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application.Tasks;
using Xunit;

namespace TaskExecution.UnitTests.Api;

public sealed class VolunteerMatchRouteTests
{
    [Fact]
    public void Matches_route_requires_leader_and_declares_dependency_failures()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddScoped<GetVolunteerMatchesHandler>();
        using var app = builder.Build();
        app.MapVolunteerMatchEndpoints();
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Single();
        endpoint.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}/volunteer-matches");
        endpoint.Metadata.GetMetadata<EndpointNameMetadata>()!.EndpointName.Should().Be("listVolunteerMatches");
        endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(policy => policy.Requirements)
            .OfType<RolesAuthorizationRequirement>().SelectMany(requirement => requirement.AllowedRoles)
            .Should().Equal("LEADER");
        endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(item => item.StatusCode)
            .Should().Contain([200, 400, 401, 403, 404, 503]);
    }
}
