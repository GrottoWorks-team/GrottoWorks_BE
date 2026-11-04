using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TaskExecution.Application.Events;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;

namespace TaskExecution.Infrastructure.Tasks;

public sealed class TaskFileGateway(HttpClient client,IConfiguration configuration,ITaskCorrelationContext correlation) : ITaskFileGateway
{
    public async Task ReserveAsync(AttachmentIntent row,CancellationToken ct)=>await SendAsync(row.FileId,"attachment-reservations",new {
        reservationId=row.Id,uploadedByUserId=row.UploaderId,service="TaskExecution",referenceType="TASK_PROGRESS",referenceId=row.ProgressId,purpose="PROGRESS_EVIDENCE" },ct);
    public async Task FinalizeAsync(AttachmentIntent row,CancellationToken ct)=>await SendAsync(row.FileId,"attachment-reservations/finalize",Command(row),ct);
    public async Task ReleaseAsync(AttachmentIntent row,CancellationToken ct)=>await SendAsync(row.FileId,"attachment-reservations/release",Command(row),ct,true);
    private static object Command(AttachmentIntent row)=>new {reservationId=row.Id,service="TaskExecution",referenceType="TASK_PROGRESS",referenceId=row.ProgressId};
    public async Task<FileDownload> DownloadAsync(Guid fileId,Guid progressId,CancellationToken ct)
    {
        var data=await SendAsync(fileId,"record-download-url",new {service="TaskExecution",referenceType="TASK_PROGRESS",referenceId=progressId},ct);
        if(!data.TryGetProperty("downloadUrl",out var url) || !data.TryGetProperty("expiresAt",out var expires) ||
            !Uri.TryCreate(url.GetString(),UriKind.Absolute,out var uri) || uri.Scheme is not ("https" or "http")) throw Unavailable();
        return new(url.GetString()!,expires.GetDateTimeOffset());
    }
    private async Task<JsonElement> SendAsync(Guid fileId,string path,object body,CancellationToken ct,bool release=false)
    {
        var key=configuration["InternalApi:ApiKey"];
        if(string.IsNullOrWhiteSpace(key) || key.Length<32) throw Unavailable();
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,$"internal/files/{fileId:D}/{path}") {Content=JsonContent.Create(body)};
            request.Headers.Add("X-Internal-Api-Key",key);request.Headers.Add("X-Correlation-Id",correlation.CorrelationId);
            using var response=await client.SendAsync(request,ct);
            if(response.StatusCode==HttpStatusCode.NoContent || (release && response.StatusCode==HttpStatusCode.NotFound)) return default;
            if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
                throw new DomainRuleException("TASK_EVIDENCE_FILE_INVALID","The file is unavailable, incomplete, already attached, or has a different uploader or purpose.",DomainErrorKind.Validation);
            if(!response.IsSuccessStatusCode) throw Unavailable();
            var envelope=await response.Content.ReadFromJsonAsync<Envelope>(ct);
            if(envelope is null || envelope.Data.ValueKind!=JsonValueKind.Object) throw Unavailable();
            return envelope.Data;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception exception) when(exception is HttpRequestException or JsonException or OperationCanceledException or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        { throw Unavailable(); }
    }
    private sealed record Envelope(JsonElement Data);
    private static DomainRuleException Unavailable()=>new("FILE_SERVICE_UNAVAILABLE","File attachment service is temporarily unavailable.",DomainErrorKind.ServiceUnavailable);
}
