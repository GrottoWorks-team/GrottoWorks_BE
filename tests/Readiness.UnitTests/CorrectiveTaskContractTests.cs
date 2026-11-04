using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Readiness.Application.Checklists;
using Readiness.Domain;
using Readiness.Infrastructure.Checklists;

namespace Readiness.UnitTests;

public sealed class CorrectiveTaskContractTests
{
    [Fact]
    public async Task Corrective_client_sends_the_exact_Task_capacity_and_idempotency_contract()
    {
        var definition=new CorrectiveTaskDefinition(Guid.NewGuid(),"Repair",null,null,"MEDIUM",60,2,null,DateTimeOffset.UtcNow.AddDays(1));
        var request=new CorrectiveTaskCreationRequest(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,null,definition);
        var transport=new Transport(request.WorkAreaId);
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {["InternalApi:ApiKey"]=new string('k',32)}).Build();
        var result=await new TaskCorrectiveClient(new HttpClient(transport){BaseAddress=new Uri("http://taskexecution.api/")},configuration).CreateAsync(request,default);
        result.WorkAreaId.Should().Be(request.WorkAreaId);
        transport.Key.Should().Be(definition.RequestId.ToString("D"));
        using var payload=JsonDocument.Parse(transport.Body!);
        payload.RootElement.GetProperty("requiredVolunteers").GetInt32().Should().Be(2);
        payload.RootElement.GetProperty("priority").GetString().Should().Be("MEDIUM");
        payload.RootElement.TryGetProperty("requiredVolunteerCount",out _).Should().BeFalse();
    }
    [Fact]
    public void Unsupported_priority_is_rejected_before_sending_a_command()
    {
        var definition=new CorrectiveTaskDefinition(Guid.NewGuid(),"Repair",null,null,"NORMAL",60,1,null,DateTimeOffset.UtcNow.AddDays(1));
        Action validate=()=>definition.Validate();validate.Should().Throw<DomainRuleException>();
    }
    private sealed class Transport(Guid area) : HttpMessageHandler
    {
        public string? Body;public string? Key;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Body=await request.Content!.ReadAsStringAsync(ct);Key=request.Headers.GetValues("Idempotency-Key").Single();
            return new HttpResponseMessage(HttpStatusCode.Created){Content=new StringContent(JsonSerializer.Serialize(new {data=new {id=Guid.NewGuid(),workAreaId=area,taskType="CORRECTIVE",status="OPEN"}}),System.Text.Encoding.UTF8,"application/json")};
        }
    }
}
