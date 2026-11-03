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

public sealed class SupportResponseRouteTests
{
    [Fact]
    public async Task Staffing_routes_preserve_operations_roles_and_response_contracts()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName="Testing"});
        builder.Services.AddScoped<SupportResponseHandler>();builder.Services.AddScoped<ListSupportResponsesHandler>();
        builder.Services.AddScoped<ReviewSupportMemberHandler>();
        await using var app=builder.Build();app.MapSupportResponseEndpoints();app.MapSupportMemberReviewEndpoints();
        var routes=((IEndpointRouteBuilder)app).DataSources.SelectMany(source=>source.Endpoints).OfType<RouteEndpoint>().ToList();
        var create=routes.Single(route=>route.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName=="respondToSupportRequest");
        create.RoutePattern.RawText.Should().Be("/api/v1/support-requests/{requestId:guid}/responses");
        Roles(create).Should().BeEquivalentTo("VOLUNTEER","MO");
        create.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(meta=>meta.StatusCode)
            .Should().Contain(new[] {201,400,401,403,404,409,412,422,503});
        Roles(routes.Single(route=>route.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName=="listSupportResponses")).Should().Equal("LEADER");
        foreach(var operation in new[] {"approveSupportMember","rejectSupportMember"})
        {
            var route=routes.Single(route=>route.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName==operation);
            Roles(route).Should().Equal("LEADER");
            route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(meta=>meta.StatusCode)
                .Should().Contain(new[] {200,400,401,403,404,409,412,422,503});
        }
    }
    private static IEnumerable<string> Roles(RouteEndpoint route)=>route.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
        .SelectMany(policy=>policy.Requirements).OfType<RolesAuthorizationRequirement>().SelectMany(requirement=>requirement.AllowedRoles);
}
