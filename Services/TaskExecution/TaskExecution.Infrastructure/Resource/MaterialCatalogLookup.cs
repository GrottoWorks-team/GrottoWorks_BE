using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Resource.Contracts.Catalog;
using TaskExecution.Application.Events;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;

namespace TaskExecution.Infrastructure.Resource;

public sealed class MaterialCatalogLookup(HttpClient client, IConfiguration configuration, ITaskCorrelationContext correlation)
    : IMaterialCatalogLookup
{
    public async Task<IReadOnlyList<InternalMaterialDto>> FindAsync(IReadOnlyCollection<Guid> materialIds, CancellationToken cancellationToken)
    {
        if (materialIds.Count == 0) return [];
        var key = configuration["InternalApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32) throw Unavailable();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "internal/materials?" + string.Join('&', materialIds.Distinct().Select(id => $"ids={id:D}")));
            request.Headers.Add("X-Internal-Api-Key", key);
            request.Headers.Add("X-Correlation-Id", correlation.CorrelationId);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            var envelope = await response.Content.ReadFromJsonAsync<Envelope>(cancellationToken);
            if (envelope?.Data is null || envelope.Data.Count > materialIds.Count ||
                envelope.Data.Select(row => row?.MaterialId).Distinct().Count() != envelope.Data.Count ||
                envelope.Data.Any(row => row is null || !materialIds.Contains(row.MaterialId) ||
                    string.IsNullOrWhiteSpace(row.Code) || string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Unit) ||
                    row.Status is not ("ACTIVE" or "INACTIVE"))) throw Unavailable();
            return envelope.Data;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException or
            Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException) { throw Unavailable(); }
    }
    private static DomainRuleException Unavailable() => new("RESOURCE_SERVICE_UNAVAILABLE", "Material catalog validation is temporarily unavailable.", DomainErrorKind.ServiceUnavailable);
    private sealed record Envelope(IReadOnlyList<InternalMaterialDto>? Data);
}
