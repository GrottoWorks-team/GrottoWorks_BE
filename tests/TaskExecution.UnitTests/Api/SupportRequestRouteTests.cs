using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application.Support;

namespace TaskExecution.UnitTests.Api;

public sealed class SupportRequestRouteTests
{
    [Fact]
    public async Task Create_and_detail_routes_expose_contract_metadata()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName="Testing"});
        builder.Services.AddScoped<SupportRequestService>();
        await using var app=builder.Build();app.MapSupportRequestEndpoints();
        var routes=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().ToList();
        var create=routes.Single(e=>e.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName=="createSupportRequest");
        create.RoutePattern.RawText.Should().Be("/api/v1/support-requests");
        create.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(p=>p.Requirements)
            .OfType<RolesAuthorizationRequirement>().Should().Contain(r=>r.AllowedRoles.Contains("LEADER"));
        create.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(p=>p.StatusCode)
            .Should().Contain(new[] {201,400,401,403,404,409,412,422,503});
        routes.Single(e=>e.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName=="getSupportRequest")
            .RoutePattern.RawText.Should().Be("/api/v1/support-requests/{requestId:guid}");
    }
}
