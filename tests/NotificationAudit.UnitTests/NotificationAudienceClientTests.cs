using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NotificationAudit.Application.Events;
using NotificationAudit.Infrastructure.Deadlines;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class NotificationAudienceClientTests
{
    [Fact]
    public async Task Material_audience_comes_from_complete_authoritative_list_not_partial_projection()
    {
        var parish = Guid.NewGuid(); var season = Guid.NewGuid(); var destination = Guid.NewGuid();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var handler = new Handler(request =>
        {
            Assert.Equal("parish.local", request.RequestUri!.Host);
            Assert.Contains($"parishId={parish}", request.RequestUri.Query);
            Assert.Equal("correlation", request.Headers.GetValues("X-Correlation-Id").Single());
            Assert.Equal(new string('k', 32), request.Headers.GetValues("X-Internal-Api-Key").Single());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = new[] {
                new { workAreaId = destination, parishId = parish, seasonId = season, materialOfficerUserId = Guid.NewGuid() },
                new { workAreaId = Guid.NewGuid(), parishId = parish, seasonId = season, materialOfficerUserId = first },
                new { workAreaId = Guid.NewGuid(), parishId = parish, seasonId = season, materialOfficerUserId = second }
            }})};
        });
        var resolver = Create(handler);
        var recipients = await resolver.ResolveAsync(Message(parish, season, destination), default);
        Assert.NotNull(recipients);
        Assert.Equal(new[] { first, second }.Order(), recipients);
    }

    [Fact]
    public async Task Missing_source_season_retains_context_instead_of_resolving_an_empty_audience()
    {
        var resolver = Create(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await resolver.ResolveAsync(Message(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default));
    }

    [Fact]
    public async Task Wrong_tenant_payload_is_not_accepted()
    {
        var resolver = Create(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = JsonContent.Create(new { data = new[] { new {
                workAreaId = Guid.NewGuid(), parishId = Guid.NewGuid(), seasonId = Guid.NewGuid()
            }}})
        }));
        Assert.Null(await resolver.ResolveAsync(Message(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default));
    }

    private static NotificationAudienceResolver Create(HttpMessageHandler handler)
    {
        var planning = new HttpClient(handler) { BaseAddress = new Uri("http://parish.local/") };
        return new NotificationAudienceResolver(new HttpClient(), new Factory(planning),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["InternalApi:ApiKey"] = new string('k',32) }).Build());
    }
    private static IncomingIntegrationEvent Message(Guid parish, Guid season, Guid destination) =>
        new(Guid.NewGuid(), "support.request.published", DateTimeOffset.UtcNow, "correlation", Guid.NewGuid(),
            parish, season, "support-request", Guid.NewGuid(), JsonSerializer.SerializeToElement(new {
                supportType = "MATERIAL", workAreaId = destination }));
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response(request));
    }
}
