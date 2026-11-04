using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NotificationAudit.Api.Endpoints;
using NotificationAudit.Application;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class NotificationContractRouteTests
{
    [Fact]
    public void Preferences_and_history_match_contract_metadata()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddRouting();
        builder.Services.AddApplication();
        using var app = builder.Build();
        app.MapNotificationAuditEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        var preferences = endpoints.Single(endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "replaceNotificationPreferences");
        preferences.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().NotBeEmpty();
        var history = endpoints.Single(endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "getResourceHistory");
        history.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(policy => policy.Requirements)
            .OfType<RolesAuthorizationRequirement>().SelectMany(requirement => requirement.AllowedRoles).Should().Equal("ADMIN");
        history.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(item => item.StatusCode).Should().Contain(400);
    }
}
