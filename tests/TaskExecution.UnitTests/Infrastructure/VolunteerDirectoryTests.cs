using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskExecution.Application.Events;
using TaskExecution.Domain;
using TaskExecution.Infrastructure.Identity;
using Xunit;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class VolunteerDirectoryTests
{
    [Fact]
    public async Task Directory_forwards_scope_and_credentials_and_reads_availability_without_notes()
    {
        var parish = Guid.NewGuid();
        var community = Guid.NewGuid();
        var user = Guid.NewGuid();
        var transport = new Transport($$$"""
            {"data":[{"userId":"{{{user}}}","displayName":"Volunteer","status":"ACTIVE","role":"VOLUNTEER",
              "parishId":"{{{parish}}}","communityId":"{{{community}}}","skillIds":[],"skills":[],
              "availability":[{"availableFrom":"2026-12-01T01:00:00Z","availableTo":"2026-12-01T03:00:00Z","note":"ignored"}]}],
             "page":{"page":1,"size":100,"totalItems":1}}
            """);
        var result = await Client(transport).GetPageAsync(parish, community, 1, 100, default);
        result.Items.Single().Availability!.Single().AvailableTo.Should().Be(DateTimeOffset.Parse("2026-12-01T03:00:00Z"));
        transport.Url.Should().Contain($"parishId={parish:D}").And.Contain($"communityId={community:D}");
        transport.Key.Should().Be(new string('k', 32));
        transport.Correlation.Should().Be("directory-test");
    }

    [Fact]
    public async Task Missing_page_metadata_returns_unavailable_instead_of_partial_success()
    {
        Func<Task> query = () => Client(new Transport("{\"data\":[]}")).GetPageAsync(Guid.NewGuid(), null, 1, 100, default);
        (await query.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.ServiceUnavailable);
    }

    private static IdentityVolunteerDirectory Client(Transport transport) => new(
        new HttpClient(transport) { BaseAddress = new("http://identity.api:8080/") },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["InternalApi:ApiKey"] = new string('k', 32) }).Build(),
        new Correlation());
    private sealed class Correlation : ITaskCorrelationContext { public string CorrelationId => "directory-test"; }
    private sealed class Transport(string body) : HttpMessageHandler
    {
        public string Url { get; private set; } = "";
        public string Key { get; private set; } = "";
        public string Correlation { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Key = request.Headers.GetValues("X-Internal-Api-Key").Single();
            Correlation = request.Headers.GetValues("X-Correlation-Id").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
