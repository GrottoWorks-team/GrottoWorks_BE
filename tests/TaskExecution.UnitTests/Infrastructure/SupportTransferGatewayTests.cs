using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Resource.Contracts.Inventory;
using TaskExecution.Application.Support;
using TaskExecution.Domain;
using TaskExecution.Infrastructure.Resource;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class SupportTransferGatewayTests
{
    [Fact]
    public async Task Command_keeps_attempt_identity_api_key_and_correlation()
    {
        var attempt=Attempt(); var transport=new Transport(HttpStatusCode.OK,JsonSerializer.Serialize(new {data=Result(attempt)}));
        var result=await Client(transport).ExecuteAsync(attempt,default);
        result.OperationId.Should().Be(attempt.OperationId); transport.Method.Should().Be(HttpMethod.Post);
        transport.Path.Should().Be("/internal/support-transfers");
        using var json=JsonDocument.Parse(transport.Body!);
        json.RootElement.GetProperty("approvalOperationId").GetGuid().Should().Be(attempt.OperationId);
        transport.Correlation.Should().Be(attempt.CorrelationId); transport.HasKey.Should().BeTrue();
    }
    [Fact]
    public async Task Only_missing_operation_returns_null_while_stock_problem_details_remains_ambiguous()
    {
        (await Client(new Transport(HttpStatusCode.NotFound,"{}")).FindDecisionAsync(Attempt(),default)).Should().BeNull();
        Func<Task> execute=()=>Client(new Transport(HttpStatusCode.Conflict,"{\"code\":\"MATERIAL_INSUFFICIENT_AVAILABLE\"}")).ExecuteAsync(Attempt(),default);
        await execute.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="RESOURCE_SERVICE_UNAVAILABLE");
    }
    [Fact]
    public async Task Rejected_operation_is_authoritative_but_mismatched_payload_is_not()
    {
        var attempt=Attempt(); var rejection=Result(attempt) with {Status="REJECTED",ErrorCode="MATERIAL_INSUFFICIENT_AVAILABLE"};
        (await Client(new Transport(HttpStatusCode.OK,JsonSerializer.Serialize(new {data=rejection}))).ExecuteAsync(attempt,default))
            .ErrorCode.Should().Be("MATERIAL_INSUFFICIENT_AVAILABLE");
        Func<Task> invalid=()=>Client(new Transport(HttpStatusCode.OK,JsonSerializer.Serialize(new {data=rejection with {Quantity=999}}))).ExecuteAsync(attempt,default);
        await invalid.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Kind==DomainErrorKind.ServiceUnavailable);
    }
    private static SupportTransferAttempt Attempt()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),5,Guid.NewGuid(),"support-approval");
    private static SupportTransferOperationResult Result(SupportTransferAttempt a)=>new(a.OperationId,a.ItemId,a.SourceWorkAreaId,a.TargetWorkAreaId,a.MaterialId,a.Quantity,a.ApproverId,"COMPLETED",null,DateTimeOffset.UtcNow);
    private static SupportTransferGateway Client(Transport transport)=>new(new HttpClient(transport) {BaseAddress=new Uri("http://resource.api:8080/")},
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {["InternalApi:ApiKey"]=new string('k',32)}).Build());
    private sealed class Transport(HttpStatusCode status,string body) : HttpMessageHandler
    {
        public HttpMethod? Method; public string? Body; public string? Path; public string? Correlation; public bool HasKey;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Method=request.Method; Path=request.RequestUri!.AbsolutePath; HasKey=request.Headers.Contains("X-Internal-Api-Key");
            Correlation=request.Headers.GetValues("X-Correlation-Id").Single();
            Body=request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) {Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")};
        }
    }
}
