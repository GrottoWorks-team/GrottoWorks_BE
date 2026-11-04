using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Resource.Contracts.Inventory;
using TaskExecution.Application.Support;
using TaskExecution.Domain;

namespace TaskExecution.Infrastructure.Resource;

public sealed class SupportTransferReceiptLookup(HttpClient client,IConfiguration configuration) : ISupportTransferReceiptLookup
{
    public async Task<SupportTransferSnapshot?> GetAsync(Guid itemId,string correlationId,CancellationToken ct)
    {
        var key=configuration["InternalApi:ApiKey"];
        if(string.IsNullOrWhiteSpace(key) || key.Length<32) throw Unavailable();
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,$"internal/support-transfers?ids={itemId:D}");
            request.Headers.Add("X-Internal-Api-Key",key); request.Headers.Add("X-Correlation-Id",correlationId);
            using var response=await client.SendAsync(request,ct);
            if(!response.IsSuccessStatusCode) throw Unavailable();
            var envelope=await response.Content.ReadFromJsonAsync<Envelope>(ct);
            if(envelope?.Data is null || envelope.Data.Count>1 || envelope.Data.Any(row=>row is null || row.SupportResponseMaterialId!=itemId)) throw Unavailable();
            return envelope.Data.SingleOrDefault();
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) {throw;}
        catch(Exception exception) when(exception is HttpRequestException or JsonException or OperationCanceledException or
            Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException) {throw Unavailable();}
    }
    private static DomainRuleException Unavailable()=>new("RESOURCE_SERVICE_UNAVAILABLE","The Resource transfer lookup is unavailable.",DomainErrorKind.ServiceUnavailable);
    private sealed record Envelope(IReadOnlyList<SupportTransferSnapshot>? Data);
}
