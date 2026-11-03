using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Resource.Contracts.Inventory;
using TaskExecution.Domain;
using TaskExecution.Infrastructure.Resource;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class SupportTransferReceiptLookupTests
{
    [Fact]
    public async Task Lookup_uses_internal_batch_route_and_preserves_receipt_facts()
    {
        var row=new SupportTransferSnapshot(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),10,
            "RECEIVED",Guid.NewGuid(),DateTimeOffset.UtcNow,8,2,Guid.NewGuid(),DateTimeOffset.UtcNow);
        var transport=new Transport(JsonSerializer.Serialize(new {data=new[] {row}}));
        (await Client(transport).GetAsync(row.SupportResponseMaterialId,"receipt-sync",default)).Should().Be(row);
        transport.Path.Should().Be($"/internal/support-transfers?ids={row.SupportResponseMaterialId:D}");
        transport.HasHeaders.Should().BeTrue();
    }
    [Theory]
    [InlineData("{}")][InlineData("invalid-json")][InlineData("{\"data\":[null]}")]
    public async Task Missing_or_malformed_envelope_is_not_an_empty_receipt(string body)
    {
        Func<Task> get=()=>Client(new Transport(body)).GetAsync(Guid.NewGuid(),"receipt-sync",default);
        await get.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="RESOURCE_SERVICE_UNAVAILABLE");
    }
    private static SupportTransferReceiptLookup Client(Transport transport)=>new(new HttpClient(transport) {BaseAddress=new Uri("http://resource.api:8080/")},
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {["InternalApi:ApiKey"]=new string('k',32)}).Build());
    private sealed class Transport(string body) : HttpMessageHandler
    {
        public string? Path; public bool HasHeaders;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Path=request.RequestUri!.PathAndQuery;
            HasHeaders=request.Headers.Contains("X-Internal-Api-Key") && request.Headers.GetValues("X-Correlation-Id").Single()=="receipt-sync";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")});
        }
    }
}
