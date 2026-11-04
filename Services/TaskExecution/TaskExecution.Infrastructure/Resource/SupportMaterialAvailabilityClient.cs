using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Resource.Contracts.Inventory;
using TaskExecution.Application.Events;
using TaskExecution.Application.Support;
using TaskExecution.Domain;

namespace TaskExecution.Infrastructure.Resource;

public sealed class SupportMaterialAvailabilityClient(HttpClient client,IConfiguration configuration,ITaskCorrelationContext correlation) : ISupportMaterialAvailability
{
    public async Task<IReadOnlyList<SupportMaterialAvailability>> GetAsync(Guid areaId,IReadOnlyCollection<Guid> ids,CancellationToken ct)
    {
        var result=new List<SupportMaterialAvailability>();
        var key=configuration["InternalApi:ApiKey"];
        if(string.IsNullOrWhiteSpace(key) || key.Length<32) throw Unavailable();
        try
        {
            foreach(var batch in ids.Distinct().Chunk(100))
            {
                var query=string.Join("&",batch.Select(id=>$"materialIds={id:D}"));
                using var request=new HttpRequestMessage(HttpMethod.Get,$"internal/work-areas/{areaId:D}/available-materials?{query}");
                request.Headers.Add("X-Internal-Api-Key",key);request.Headers.Add("X-Correlation-Id",correlation.CorrelationId);
                using var response=await client.SendAsync(request,ct);
                if(!response.IsSuccessStatusCode) throw Unavailable();
                var envelope=await response.Content.ReadFromJsonAsync<Envelope>(ct);
                if(envelope?.Data is null || envelope.Data.Any(row=>row is null || row.WorkAreaId!=areaId || !batch.Contains(row.MaterialId) ||
                    row.ParishId==Guid.Empty || row.SeasonId==Guid.Empty || row.ScopeVersion<=0 || row.CalculatedAt==default ||
                    row.MaterialStatus is not ("ACTIVE" or "INACTIVE") || row.UnallocatedQuantity<0) ||
                    envelope.Data.Select(row=>row.MaterialId).Distinct().Count()!=envelope.Data.Count) throw Unavailable();
                result.AddRange(envelope.Data.Select(row=>new SupportMaterialAvailability(row.WorkAreaId,row.ParishId,row.SeasonId,row.MaterialOfficerUserId,
                    row.ScopeVersion,row.MaterialId,row.MaterialStatus,row.UnallocatedQuantity,row.CalculatedAt)));
            }
            return result;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) {throw;}
        catch(Exception exception) when(exception is HttpRequestException or JsonException or OperationCanceledException or
            Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException) {throw Unavailable();}
    }
    private static DomainRuleException Unavailable()=>new("RESOURCE_SERVICE_UNAVAILABLE","Resource stock validation is temporarily unavailable.",DomainErrorKind.ServiceUnavailable);
    private sealed record Envelope(IReadOnlyList<AvailableSupportMaterial>? Data);
}
