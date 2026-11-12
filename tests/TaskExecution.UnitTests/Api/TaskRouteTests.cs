using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
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
        builder.Services.AddScoped<TaskStartEligibilityService>();
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
        var eligibility = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "getTaskStartEligibility");

        list.RoutePattern.RawText.Should().Be("/api/v1/work-areas/{workAreaId:guid}/tasks");
        detail.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}");
        // 12/11/2026: điều kiện bắt đầu việc (giờ bắt đầu / thành viên / vật tư đã cấp).
        eligibility.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}/start-eligibility");
        eligibility.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
        eligibility.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404]);
        list.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
        detail.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
        list.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404]);
        detail.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404]);
        create.RoutePattern.RawText.Should().Be("/api/v1/work-areas/{workAreaId:guid}/tasks");
        Roles(create).Should().Equal(GrottoWorksRoles.Leader);
        create.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([201, 400, 401, 403, 404, 409, 422]);
        update.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}");
        Roles(update).Should().Equal(GrottoWorksRoles.Leader);
        update.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404, 409, 412, 422]);
        criteria.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}/completion-criteria");
        Roles(criteria).Should().Equal(GrottoWorksRoles.Leader);
        criteria.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404, 409, 412, 422]);
        skills.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}/skills");
        Roles(skills).Should().Equal(GrottoWorksRoles.Leader);
        skills.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404, 409, 412, 422]);
        ready.RoutePattern.RawText.Should().Be("/api/v1/tasks/{taskId:guid}/ready");
        Roles(ready).Should().Equal(GrottoWorksRoles.Leader);
        ready.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode).Should().Contain([200, 400, 401, 403, 404, 409, 412]);
    }
    private static IEnumerable<string> Roles(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
            .SelectMany(policy => policy.Requirements)
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(requirement => requirement.AllowedRoles);
}
