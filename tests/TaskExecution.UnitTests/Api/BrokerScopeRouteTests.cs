using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application;
using TaskExecution.Application.Scopes;

namespace TaskExecution.UnitTests.Api;

public sealed class BrokerScopeRouteTests
{
    [Fact]
    public async Task Task_routes_exclude_legacy_scope_puts_but_keep_internal_counts_and_broker_handlers()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);
        builder.Services.AddApplication();
        await using var app = builder.Build();
        app.MapTaskExecutionEndpoints();
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).ToList();

        routes.Should().NotContain(route => route != null &&
            (route.Contains("/season-scopes") || route.Contains("/work-area-scopes")));
        routes.Should().Contain("/internal/work-area-task-counts");
        builder.Services.Should().Contain(service => service.ServiceType == typeof(ApplySeasonScopeSnapshotHandler));
        builder.Services.Should().Contain(service => service.ServiceType == typeof(ApplyWorkAreaScopeSnapshotHandler));
        typeof(TaskExecutionEndpoints).Assembly.GetTypes().Should().NotContain(type =>
            type.Name == "SeasonScopeSnapshotRequest" || type.Name == "WorkAreaScopeSnapshotRequest" || type.Name == "InternalScopeEndpoints");
    }
}
