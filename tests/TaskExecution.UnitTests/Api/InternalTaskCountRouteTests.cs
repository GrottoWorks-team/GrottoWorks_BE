using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskExecution.Api.Common;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application.Tasks;

namespace TaskExecution.UnitTests.Api;

public sealed class InternalTaskCountRouteTests
{
    private const string ApiKey = "task-count-test-key-with-32-characters";

    [Theory]
    [InlineData(null, ApiKey, HttpStatusCode.Unauthorized)]
    [InlineData("incorrect-key", ApiKey, HttpStatusCode.Unauthorized)]
    [InlineData(ApiKey, null, HttpStatusCode.Unauthorized)]
    [InlineData("short-key", "short-key", HttpStatusCode.Unauthorized)]
    [InlineData(ApiKey, ApiKey, HttpStatusCode.OK)]
    public async Task Route_enforces_internal_authentication_over_http_and_is_hidden_from_openapi(
        string? presentedKey, string? configuredKey, HttpStatusCode expectedStatus)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders().AddConsole();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InternalApiKeyFilter.ConfigurationKey] = configuredKey
        });
        var reader = new CountReader();
        builder.Services.AddSingleton<IWorkAreaTaskCountReader>(reader);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<GetWorkAreaTaskCountsQueryHandler>();
        await using var app = builder.Build();
        app.MapInternalTaskCountEndpoints();
        await app.StartAsync();

        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName ==
                                 "internalGetWorkAreaTaskCounts");
        endpoint.RoutePattern.RawText.Should().Be("/internal/work-area-task-counts");
        endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription.Should().BeTrue();

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        if (presentedKey is not null)
            client.DefaultRequestHeaders.Add(InternalApiKeyFilter.HeaderName, presentedKey);
        var workAreaId = Guid.NewGuid();
        using var response = await client.GetAsync($"/internal/work-area-task-counts?workAreaIds={workAreaId}");

        response.StatusCode.Should().Be(expectedStatus);
        if (expectedStatus == HttpStatusCode.OK)
        {
            reader.RequestedIds.Should().Equal(workAreaId);
        }
        else
        {
            reader.RequestedIds.Should().BeEmpty("unauthorized requests must not execute the query");
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().Should().Be("INTERNAL_AUTH_REQUIRED");
        }
    }

    private sealed class CountReader : IWorkAreaTaskCountReader
    {
        public IReadOnlyCollection<Guid> RequestedIds { get; private set; } = [];

        public Task<IReadOnlyList<WorkAreaTaskCountValues>> GetAsync(
            IReadOnlyCollection<Guid> workAreaIds, DateTimeOffset calculatedAt, CancellationToken cancellationToken = default)
        {
            RequestedIds = workAreaIds;
            return Task.FromResult<IReadOnlyList<WorkAreaTaskCountValues>>([]);
        }
    }
}
