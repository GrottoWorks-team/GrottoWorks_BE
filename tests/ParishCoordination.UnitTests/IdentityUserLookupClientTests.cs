using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ParishCoordination.Application.Users;
using ParishCoordination.Infrastructure.Identity;

namespace ParishCoordination.UnitTests;

public sealed class IdentityUserLookupClientTests
{
    private static readonly Guid UserId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string ApiKey = "unit-test-internal-api-key-123456789";

    [Fact]
    public async Task Client_sends_internal_key_and_correlation_id_and_maps_user()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            $$"""
            {
              "data": [
                {
                  "userId": "{{UserId}}",
                  "displayName": "Leader",
                  "status": "ACTIVE",
                  "role": "LEADER",
                  "parishId": "{{ParishId}}",
                  "communityId": null,
                  "skillIds": []
                }
              ],
              "meta": { "correlationId": "test-correlation" }
            }
            """));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Request.Headers["X-Correlation-Id"] = "test-correlation";
        var client = CreateClient(handler, accessor);

        var user = await client.GetByIdAsync(UserId);

        user.Should().Be(new IdentityUser(
            UserId,
            IdentityUserStatus.Active,
            "LEADER",
            ParishId));
        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri.Should().Be($"http://identity.test/internal/users?ids={UserId:D}");
        handler.InternalApiKey.Should().Be(ApiKey);
        handler.CorrelationId.Should().Be("test-correlation");
    }

    [Fact]
    public async Task Empty_identity_result_is_treated_as_missing_user()
    {
        var client = CreateClient(
            new RecordingHandler(_ => JsonResponse(
                """{ "data": [], "meta": { "correlationId": "test" } }""")));

        var user = await client.GetByIdAsync(UserId);

        user.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Unsuccessful_response_is_reported_as_unavailable(HttpStatusCode statusCode)
    {
        var client = CreateClient(
            new RecordingHandler(_ => new HttpResponseMessage(statusCode)));

        var action = () => client.GetByIdAsync(UserId);

        await action.Should().ThrowAsync<IdentityUserLookupUnavailableException>();
    }

    [Fact]
    public async Task Network_failure_and_invalid_payload_are_reported_as_unavailable()
    {
        var networkClient = CreateClient(
            new RecordingHandler(_ => throw new HttpRequestException("simulated outage")));
        var invalidPayloadClient = CreateClient(
            new RecordingHandler(_ => JsonResponse("{ \"data\": null }")));

        var networkAction = () => networkClient.GetByIdAsync(UserId);
        var payloadAction = () => invalidPayloadClient.GetByIdAsync(UserId);

        await networkAction.Should().ThrowAsync<IdentityUserLookupUnavailableException>();
        await payloadAction.Should().ThrowAsync<IdentityUserLookupUnavailableException>();
    }

    [Fact]
    public async Task Missing_internal_api_key_fails_without_sending_request()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{ \"data\": [] }"));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://identity.test/")
        };
        var client = new IdentityUserLookupClient(
            httpClient,
            new IdentityClientOptions(httpClient.BaseAddress, string.Empty),
            new HttpContextAccessor(),
            NullLogger<IdentityUserLookupClient>.Instance);

        var action = () => client.GetByIdAsync(UserId);

        await action.Should().ThrowAsync<IdentityUserLookupUnavailableException>();
        handler.CallCount.Should().Be(0);
    }

    private static IdentityUserLookupClient CreateClient(
        RecordingHandler handler,
        HttpContextAccessor? accessor = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://identity.test/")
        };

        return new IdentityUserLookupClient(
            httpClient,
            new IdentityClientOptions(httpClient.BaseAddress, ApiKey),
            accessor ?? new HttpContextAccessor(),
            NullLogger<IdentityUserLookupClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? RequestUri { get; private set; }
        public string? InternalApiKey { get; private set; }
        public string? CorrelationId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri?.ToString();
            InternalApiKey = request.Headers.TryGetValues("X-Internal-Api-Key", out var apiKeys)
                ? apiKeys.Single()
                : null;
            CorrelationId = request.Headers.TryGetValues("X-Correlation-Id", out var correlationIds)
                ? correlationIds.Single()
                : null;

            return Task.FromResult(responseFactory(request));
        }
    }
}
