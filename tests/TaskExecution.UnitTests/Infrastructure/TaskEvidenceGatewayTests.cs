using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskExecution.Application.Events;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Infrastructure.Tasks;
using TaskExecution.Infrastructure.Resource;

namespace TaskExecution.UnitTests.Infrastructure;
public sealed class TaskEvidenceGatewayTests
{
    [Fact]
    public async Task Reservation_sends_stable_identity_record_uploader_and_purpose()
    {
        var handler=new Handler();var intent=new AttachmentIntent(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow);
        var gateway=new TaskFileGateway(Client(handler),Configuration(),new Correlation());
        await gateway.ReserveAsync(intent,default);
        handler.Path.Should().Be($"/internal/files/{intent.FileId:D}/attachment-reservations");
        using var body=JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("reservationId").GetGuid().Should().Be(intent.Id);
        body.RootElement.GetProperty("referenceId").GetGuid().Should().Be(intent.ProgressId);
        body.RootElement.GetProperty("uploadedByUserId").GetGuid().Should().Be(intent.UploaderId);
        body.RootElement.GetProperty("purpose").GetString().Should().Be("PROGRESS_EVIDENCE");
        handler.Key.Should().Be(new string('k',32));
    }
    [Fact]
    public async Task Attachment_dependency_failure_is_not_success_or_validation_failure()
    {
        var handler=new Handler {Status=HttpStatusCode.ServiceUnavailable};
        var gateway=new TaskFileGateway(Client(handler),Configuration(),new Correlation());
        var action=()=>gateway.FinalizeAsync(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow),default);
        (await action.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.ServiceUnavailable);
    }
    [Fact]
    public async Task Missing_material_summary_payload_does_not_become_empty_material_list()
    {
        var handler=new Handler {Response="{\"data\":{}}"};
        var gateway=new TaskResourceGateway(Client(handler),Configuration(),new Correlation());
        var action=()=>gateway.MaterialSummaryAsync(Guid.NewGuid(),default);
        (await action.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.ServiceUnavailable);
    }
    [Fact]
    public async Task Release_of_missing_file_is_idempotent()
    {
        var handler=new Handler {Status=HttpStatusCode.NotFound};
        var gateway=new TaskFileGateway(Client(handler),Configuration(),new Correlation());
        await gateway.ReleaseAsync(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow),default);
    }
    private static IConfiguration Configuration()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"InternalApi:ApiKey",new string('k',32)}}).Build();
    private static HttpClient Client(Handler handler)=>new(handler){BaseAddress=new Uri("https://dependency.test/")};
    private sealed class Correlation : ITaskCorrelationContext {public string CorrelationId=>"gateway-test";}
    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status {get;set;}=HttpStatusCode.OK;public string Response {get;set;}="{\"data\":{\"finalized\":false}}";
        public string? Path {get;private set;}public string? Body {get;private set;}public string? Key {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Path=request.RequestUri!.AbsolutePath;Body=request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Key=request.Headers.GetValues("X-Internal-Api-Key").Single();
            return new(Status){Content=new StringContent(Response,Encoding.UTF8,"application/json")};
        }
    }
}
