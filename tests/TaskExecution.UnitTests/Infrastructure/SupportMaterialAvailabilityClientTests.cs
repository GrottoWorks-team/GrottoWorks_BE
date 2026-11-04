using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Resource.Contracts.Inventory;
using TaskExecution.Application.Events;
using TaskExecution.Domain;
using TaskExecution.Infrastructure.Resource;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class SupportMaterialAvailabilityClientTests
{
    [Fact]
    public async Task Lookup_forwards_key_correlation_and_reads_the_requested_stock()
    {
        var area=Guid.NewGuid();var material=Guid.NewGuid();
        var row=new AvailableSupportMaterial(area,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1,material,"ACTIVE",12,DateTimeOffset.UtcNow);
        var transport=new Transport(JsonSerializer.Serialize(new {data=new[] {row}}));
        var result=await Client(transport).GetAsync(area,[material],default);
        result.Should().ContainSingle().Which.UnallocatedQuantity.Should().Be(12);
        transport.Uri.Should().Contain($"/internal/work-areas/{area}/available-materials?materialIds={material}");transport.Headers.Should().BeTrue();
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    public async Task Invalid_upstream_payload_returns_service_unavailable(string body)
    {
        Func<Task> get=()=>Client(new Transport(body)).GetAsync(Guid.NewGuid(),[Guid.NewGuid()],default);
        await get.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="RESOURCE_SERVICE_UNAVAILABLE");
    }
    private static SupportMaterialAvailabilityClient Client(Transport transport)=>new(new HttpClient(transport) {BaseAddress=new Uri("http://resource.api:8080/")},
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["InternalApi:ApiKey"]=new string('k',32) }).Build(),new Correlation());
    private sealed class Correlation : ITaskCorrelationContext {public string CorrelationId=>"material-offer";}
    private sealed class Transport(string body) : HttpMessageHandler
    {
        public string Uri {get;private set;}="";public bool Headers {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Uri=request.RequestUri!.ToString();Headers=request.Headers.Contains("X-Internal-Api-Key") && request.Headers.GetValues("X-Correlation-Id").Single()=="material-offer";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")});
        }
    }
}
