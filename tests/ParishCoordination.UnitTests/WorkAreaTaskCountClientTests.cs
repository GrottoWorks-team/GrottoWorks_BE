using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Infrastructure.TaskExecution;

namespace ParishCoordination.UnitTests;

public sealed class WorkAreaTaskCountClientTests
{
    private static readonly Guid WorkAreaId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string ApiKey = "unit-test-internal-api-key-123456789";

    [Fact]
    public async Task Client_sends_expected_query_headers_and_maps_envelope()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            $$"""
            {
              "data": [{
                "workAreaId": "{{WorkAreaId}}",
                "totalTasks": 11,
                "cancelledTasks": 1,
                "eligibleTasks": 10,
                "completedTasks": 6,
                "overdueTasks": 2,
                "calculatedAt": "2026-10-30T12:00:00Z"
              }]
            }
            """));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Request.Headers["X-Correlation-Id"] = "correlation-123";
        var client = CreateClient(handler, accessor);

        var result = await client.GetAsync(WorkAreaId);

        result.EligibleTasks.Should().Be(10);
        result.CompletedTasks.Should().Be(6);
        handler.RequestUri.Should().Be(
            $"http://taskexecution.test/internal/work-area-task-counts?workAreaIds={WorkAreaId:D}");
        handler.InternalApiKey.Should().Be(ApiKey);
        handler.CorrelationId.Should().Be("correlation-123");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Http_errors_are_reported_as_dependency_unavailable(HttpStatusCode statusCode)
    {
        var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(statusCode)));

        var action = () => client.GetAsync(WorkAreaId);

        await action.Should().ThrowAsync<WorkAreaTaskCountUnavailableException>();
    }

    [Fact]
    public async Task Invalid_or_inconsistent_payload_is_rejected()
    {
        var client = CreateClient(new RecordingHandler(_ => JsonResponse(
            $$"""
            { "data": [{
              "workAreaId": "{{WorkAreaId}}",
              "totalTasks": 11,
              "cancelledTasks": 1,
              "eligibleTasks": 11,
              "completedTasks": 6,
              "overdueTasks": 2,
              "calculatedAt": "2026-10-30T12:00:00Z"
            }] }
            """)));

        var action = () => client.GetAsync(WorkAreaId);

        await action.Should().ThrowAsync<WorkAreaTaskCountUnavailableException>();
    }

    private static WorkAreaTaskCountClient CreateClient(
        RecordingHandler handler,
        HttpContextAccessor? accessor = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://taskexecution.test/")
        };

        return new WorkAreaTaskCountClient(
            httpClient,
            new TaskExecutionClientOptions(httpClient.BaseAddress, ApiKey),
            accessor ?? new HttpContextAccessor(),
            NullLogger<WorkAreaTaskCountClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? InternalApiKey { get; private set; }
        public string? CorrelationId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            InternalApiKey = Header(request, "X-Internal-Api-Key");
            CorrelationId = Header(request, "X-Correlation-Id");
            return Task.FromResult(responseFactory(request));
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
