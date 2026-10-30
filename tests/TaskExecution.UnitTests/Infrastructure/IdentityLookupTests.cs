using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskExecution.Application.Events;
using TaskExecution.Domain;
using TaskExecution.Infrastructure.Identity;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class IdentityLookupTests
{
    [Fact]
    public async Task Lookup_forwards_internal_key_correlation_and_reads_only_requested_user()
    {
        var user = Guid.NewGuid();
        var handler = new StubHandler($$"""{"data":[{"userId":"{{user}}","displayName":"Volunteer","status":"ACTIVE","role":"VOLUNTEER","parishId":null,"communityId":null,"skillIds":[]}]}""");
        var lookup = Create(handler);
        var result = await lookup.FindAsync(user);
        result!.UserId.Should().Be(user);
        handler.Uri.Should().EndWith($"internal/users?ids={user}");
        handler.Correlation.Should().Be("lookup-test");
        handler.HasKey.Should().BeTrue();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{invalid}")]
    public async Task Invalid_payload_maps_to_service_unavailable(string body)
    {
        var act = () => Create(new StubHandler(body)).FindAsync(Guid.NewGuid());
        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.ServiceUnavailable);
    }

    private static IdentityUserLookup Create(StubHandler handler) => new(
        new HttpClient(handler) {BaseAddress=new Uri("http://identity.api:8080/")},
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["InternalApi:ApiKey"]=new string('k',32) }).Build(),
        new Correlation());
    private sealed class Correlation : ITaskCorrelationContext {public string CorrelationId=>"lookup-test";}
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string Uri {get;private set;}="";
        public string Correlation {get;private set;}="";
        public bool HasKey {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Uri=request.RequestUri!.ToString();
            Correlation=request.Headers.GetValues("X-Correlation-Id").Single();
            HasKey=request.Headers.Contains("X-Internal-Api-Key");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")});
        }
    }
}
