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

public sealed class TaskMaterialRouteTests
{
    [Fact]
    public void Replace_materials_declares_leader_policy_concurrency_and_dependency_errors()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddScoped<ReplaceTaskMaterialsHandler>();
        using var app = builder.Build();
        app.MapTaskMaterialEndpoints();
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single();
        endpoint.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}/materials");
        endpoint.Metadata.GetMetadata<EndpointNameMetadata>()!.EndpointName.Should().Be("replaceTaskMaterials");
        endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(policy => policy.Requirements)
            .OfType<RolesAuthorizationRequirement>().SelectMany(requirement => requirement.AllowedRoles).Should().Equal("LEADER");
        endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(item => item.StatusCode)
            .Should().Contain([200, 400, 401, 403, 404, 409, 412, 422, 503]);
    }
}
