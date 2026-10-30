using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application.Tasks;
using BuildingBlocks.Security;

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
        builder.Services.AddScoped<CreateTaskCommandHandler>();
        builder.Services.AddScoped<UpdateTaskCommandHandler>();
        builder.Services.AddScoped<ReplaceCompletionCriteriaCommandHandler>();
        builder.Services.AddScoped<ReplaceTaskSkillsCommandHandler>();
        builder.Services.AddScoped<ReadyTaskCommandHandler>();
        using var app = builder.Build();
        app.MapTaskEndpoints();
        await app.StartAsync();

        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var list = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "listTasks");
        var detail = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "getTask");
        var create = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "createTask");
        var update = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "updateTask");
        var criteria = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "replaceCompletionCriteria");
        var skills = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "replaceTaskSkills");
        var ready = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "markTaskReady");

        list.RoutePattern.RawText.Should().Be("/api/v1/work-areas/{workAreaId}/tasks");
        detail.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId}");
        list.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
        detail.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
        list.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404]);
        detail.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404]);
        create.RoutePattern.RawText.Should().Be("/api/v1/work-areas/{workAreaId}/tasks");
        create.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(data => data.Roles == GrottoWorksRoles.Leader);
        create.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([201, 400, 401, 403, 404, 409, 422]);
        update.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId}");
        update.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(data => data.Roles == GrottoWorksRoles.Leader);
        update.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404, 409, 412, 422]);
        criteria.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId}/completion-criteria");
        criteria.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(data => data.Roles == GrottoWorksRoles.Leader);
        criteria.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404, 409, 412, 422]);
        skills.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId}/skills");
        skills.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(data => data.Roles == GrottoWorksRoles.Leader);
        skills.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404, 409, 412, 422]);
        ready.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}/ready");
        ready.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(data => data.Roles == GrottoWorksRoles.Leader);
        ready.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404, 409, 412]);
    }
}
