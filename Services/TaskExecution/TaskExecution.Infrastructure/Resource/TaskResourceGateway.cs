using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TaskExecution.Application.Events;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;

namespace TaskExecution.Infrastructure.Resource;

public sealed class TaskResourceGateway(HttpClient client,IConfiguration configuration,ITaskCorrelationContext correlation) : ITaskResourceGateway
{
    public async Task<PurchaseRequestSnapshot> PurchaseRequestAsync(Guid id,CancellationToken ct)
    {
        var data=await GetAsync($"internal/purchase-requests/{id:D}",ct);
        if(!data.TryGetProperty("purchaseRequestId",out var requestId) || !requestId.TryGetGuid(out var actualId) || actualId!=id ||
            !data.TryGetProperty("workAreaId",out var area) || !area.TryGetGuid(out var areaId) || areaId==Guid.Empty ||
            !data.TryGetProperty("status",out var status)) throw Unavailable();
        var version=data.TryGetProperty("version",out var v) && v.TryGetInt64(out var value) ? value : 0;
        if(!data.TryGetProperty("purchases",out var purchases) || purchases.ValueKind!=JsonValueKind.Array) throw Unavailable();
        var deliveries=purchases.Deserialize<PurchaseDeliverySnapshot[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw Unavailable();
        if(deliveries.Any(row=>row.PurchaseId==Guid.Empty || row.TaskId==Guid.Empty || row.Status=="CONFIRMED" &&
            (!row.ConfirmedAt.HasValue || row.ConfirmedByUserId is null || row.ConfirmedByUserId==Guid.Empty || row.BuyerUserId is null || row.BuyerUserId==Guid.Empty))) throw Unavailable();
        return new(actualId,areaId,status.GetString() ?? "",version,deliveries);
    }
    public async Task<JsonElement> MaterialSummaryAsync(Guid taskId,CancellationToken ct)
    {
        var data=await GetAsync($"internal/tasks/{taskId:D}/material-summary",ct);
        if(!data.TryGetProperty("taskId",out var id) || !id.TryGetGuid(out var actual) || actual!=taskId ||
            !data.TryGetProperty("workAreaId",out var area) || !area.TryGetGuid(out var areaId) || areaId==Guid.Empty ||
            !data.TryGetProperty("materials",out var materials) || materials.ValueKind!=JsonValueKind.Array) throw Unavailable();
        return data;
    }
    private async Task<JsonElement> GetAsync(string path,CancellationToken ct)
    {
        var key=configuration["InternalApi:ApiKey"];
        if(string.IsNullOrWhiteSpace(key) || key.Length<32) throw Unavailable();
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,path);
            request.Headers.Add("X-Internal-Api-Key",key);request.Headers.Add("X-Correlation-Id",correlation.CorrelationId);
            using var response=await client.SendAsync(request,ct);
            if(response.StatusCode==HttpStatusCode.NotFound && path.StartsWith("internal/purchase-requests/"))
                throw DomainRuleException.NotFound("PURCHASE_REQUEST_NOT_FOUND","The purchase request does not exist.");
            if(!response.IsSuccessStatusCode) throw Unavailable();
            var envelope=await response.Content.ReadFromJsonAsync<Envelope>(ct);
            if(envelope?.Data.ValueKind!=JsonValueKind.Object) throw Unavailable();return envelope.Data;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception ex) when(ex is HttpRequestException or JsonException or OperationCanceledException or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException) {throw Unavailable();}
    }
    private sealed record Envelope(JsonElement Data);
    private static DomainRuleException Unavailable()=>new("RESOURCE_SERVICE_UNAVAILABLE","Resource review data is temporarily unavailable.",DomainErrorKind.ServiceUnavailable);
}
