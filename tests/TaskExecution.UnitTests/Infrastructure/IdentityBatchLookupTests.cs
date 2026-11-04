using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskExecution.Application.Events;
using TaskExecution.Domain;
using TaskExecution.Infrastructure.Identity;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class IdentityBatchLookupTests
{
    [Fact]
    public async Task Member_lookup_batches_one_hundred_ids_deduplicates_and_forwards_authentication()
    {
        var ids=Enumerable.Range(0,205).Select(_=>Guid.NewGuid()).ToArray();
        var transport=new Transport();var lookup=Create(transport);
        var result=await lookup.FindManyAsync(ids.Concat(ids.Take(5)).ToArray());
        result.Keys.Should().BeEquivalentTo(ids);
        transport.Batches.Select(batch=>batch.Length).Should().Equal(100,100,5);
        transport.Headers.Should().OnlyContain(valid=>valid);
    }

    [Fact]
    public async Task Unrequested_or_duplicate_upstream_identity_is_dependency_failure()
    {
        var transport=new Transport {ForeignIdentity=true};
        Func<Task> lookup=()=>Create(transport).FindManyAsync([Guid.NewGuid()]);
        await lookup.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="IDENTITY_SERVICE_UNAVAILABLE");
    }

    private static IdentityUserLookup Create(Transport transport)=>new(
        new HttpClient(transport) {BaseAddress=new Uri("http://identity.api:8080/")},
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["InternalApi:ApiKey"]=new string('k',32) }).Build(),new Correlation());
    private sealed class Correlation : ITaskCorrelationContext {public string CorrelationId=>"support-batch";}
    private sealed class Transport : HttpMessageHandler
    {
        public List<Guid[]> Batches {get;}=[];public List<bool> Headers {get;}=[];public bool ForeignIdentity {get;init;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var ids=request.RequestUri!.Query.TrimStart('?').Split('&').Select(part=>Guid.Parse(part[4..])).ToArray();Batches.Add(ids);
            Headers.Add(request.Headers.Contains("X-Internal-Api-Key") && request.Headers.GetValues("X-Correlation-Id").Single()=="support-batch");
            var body=JsonSerializer.Serialize(new {data=ids.Select(id=>new
            {userId=ForeignIdentity ? Guid.NewGuid() : id,displayName="Volunteer",status="ACTIVE",role="VOLUNTEER",skillIds=Array.Empty<Guid>(),skills=Array.Empty<object>()})});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")});
        }
    }
}
