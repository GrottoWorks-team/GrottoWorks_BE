using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Api.Common;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application.Tasks;

namespace TaskExecution.UnitTests.Api;

public sealed class InternalTaskCountRouteTests
{
    [Fact]
    public async Task Route_is_internal_authenticated_and_hidden_from_openapi()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddRouting();
        builder.Services.AddScoped<GetWorkAreaTaskCountsQueryHandler>();
        using var app = builder.Build();
        app.MapInternalTaskCountEndpoints();
        await app.StartAsync();

        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName ==
                                 "internalGetWorkAreaTaskCounts");

        endpoint.RoutePattern.RawText.Should().Be("/internal/work-area-task-counts");
        endpoint.Metadata.GetMetadata<InternalApiKeyFilter>().Should().NotBeNull();
        endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription.Should().BeTrue();
    }
}
