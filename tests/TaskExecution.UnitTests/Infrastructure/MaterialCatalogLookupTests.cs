using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Resource.Contracts.Catalog;
using TaskExecution.Application.Events;
using TaskExecution.Domain;
using TaskExecution.Infrastructure.Resource;
using Xunit;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class MaterialCatalogLookupTests
{
    [Fact]
    public async Task Lookup_sends_one_batch_with_service_key_and_correlation()
    {
        var id = Guid.NewGuid();
        var transport = new Transport(JsonSerializer.Serialize(new { data = new[] { new InternalMaterialDto(id, "WOOD", "Wood", "piece", "ACTIVE") } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var result = await Client(transport).FindAsync([id], default);
        result.Single().MaterialId.Should().Be(id);
        transport.Url.Should().EndWith($"internal/materials?ids={id:D}");
        transport.Correlation.Should().Be("materials-test");
        transport.HasKey.Should().BeTrue();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{invalid}")]
    public async Task Invalid_dependency_payload_is_unavailable(string body)
    {
        Func<Task> query = () => Client(new Transport(body)).FindAsync([Guid.NewGuid()], default);
        (await query.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("RESOURCE_SERVICE_UNAVAILABLE");
    }
    private static MaterialCatalogLookup Client(Transport transport) => new(
        new HttpClient(transport) { BaseAddress = new("http://resource.api:8080/") },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["InternalApi:ApiKey"] = new string('k', 32) }).Build(), new Correlation());
    private sealed class Correlation : ITaskCorrelationContext { public string CorrelationId => "materials-test"; }
    private sealed class Transport(string body) : HttpMessageHandler
    {
        public string Url { get; private set; } = "";
        public string? Correlation { get; private set; }
        public bool HasKey { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Correlation = request.Headers.GetValues("X-Correlation-Id").Single();
            HasKey = request.Headers.Contains("X-Internal-Api-Key");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
