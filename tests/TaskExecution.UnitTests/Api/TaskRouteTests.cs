using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application.Tasks;

namespace TaskExecution.UnitTests.Api;

public sealed class TaskRouteTests
{
    [Fact]
    public async Task Query_routes_have_contract_paths_names_authentication_and_responses()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddRouting();
        builder.Services.AddScoped<GetTasksQueryHandler>();
        builder.Services.AddScoped<GetTaskQueryHandler>();
        using var app = builder.Build();
        app.MapTaskEndpoints();
        await app.StartAsync();

        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var list = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "listTasks");
        var detail = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "getTask");

        list.RoutePattern.RawText.Should().Be("/api/v1/work-areas/{workAreaId}/tasks");
        detail.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId}");
        list.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
        detail.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
        list.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404]);
        detail.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404]);
    }
}
