using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application.Assignments;

namespace TaskExecution.UnitTests.Api;

public sealed class AssignmentRouteTests
{
    [Fact]
    public async Task Direct_assignment_routes_require_leader_and_declare_contract_errors()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName="Testing"});
        builder.Services.AddRouting();
        builder.Services.AddScoped<AssignmentService>();
        await using var app = builder.Build();
        app.MapAssignmentEndpoints();
        app.MapApplicationEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source=>source.Endpoints).OfType<RouteEndpoint>().ToList();
        var create = endpoints.Single(endpoint=>endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName=="assignVolunteer");
        create.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}/assignments");
        create.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(policy=>policy.Requirements)
            .OfType<RolesAuthorizationRequirement>().Should().Contain(requirement=>requirement.AllowedRoles.Contains("LEADER"));
        create.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(data=>data.StatusCode)
            .Should().Contain(new[] {201,400,401,403,404,409,412,422,503});
        endpoints.Should().Contain(endpoint=>endpoint.Metadata.GetMetadata<EndpointNameMetadata>()!.EndpointName=="listTaskAssignments");
        foreach (var name in new[] { "applyForTask", "listTaskApplications" })
        {
            var route = endpoints.Single(e => e.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == name);
            route.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}/applications");
            var expectedRole = name == "applyForTask" ? "VOLUNTEER" : "LEADER";
            route.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(p => p.Requirements)
                .OfType<RolesAuthorizationRequirement>().Should().Contain(r => r.AllowedRoles.Contains(expectedRole));
        }
        foreach(var name in new[] {"acceptTaskAssignment","declineTaskAssignment"})
        {
            var response=endpoints.Single(endpoint=>endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName==name);
            response.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(policy=>policy.Requirements)
                .OfType<RolesAuthorizationRequirement>().Should().Contain(requirement=>requirement.AllowedRoles.Contains("VOLUNTEER"));
            response.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(data=>data.StatusCode)
                .Should().Contain(new[] {200,400,401,403,404,409,412,422,503});
        }
    }
}
