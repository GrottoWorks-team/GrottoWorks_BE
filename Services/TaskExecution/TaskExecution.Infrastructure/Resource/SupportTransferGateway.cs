using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Resource.Contracts.Inventory;
using TaskExecution.Application.Support;
using TaskExecution.Domain;

namespace TaskExecution.Infrastructure.Resource;

public sealed class SupportTransferGateway(HttpClient client, IConfiguration configuration) : ISupportTransferGateway
{
    public Task<SupportTransferDecision?> FindDecisionAsync(SupportTransferAttempt attempt, CancellationToken ct) => SendAsync(attempt,false,ct);
    public async Task<SupportTransferDecision> ExecuteAsync(SupportTransferAttempt attempt, CancellationToken ct) =>
        await SendAsync(attempt,true,ct) ?? throw Unavailable();

    private async Task<SupportTransferDecision?> SendAsync(SupportTransferAttempt attempt, bool execute, CancellationToken ct)
    {
        var key=configuration["InternalApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length<32) throw Unavailable();
        try
        {
            using var request=new HttpRequestMessage(execute ? HttpMethod.Post : HttpMethod.Get,
                execute ? "internal/support-transfers" : $"internal/support-transfer-operations/{attempt.OperationId:D}");
            request.Headers.Add("X-Internal-Api-Key",key); request.Headers.Add("X-Correlation-Id",attempt.CorrelationId);
            if (execute) request.Content=JsonContent.Create(new
            {
                approvalOperationId=attempt.OperationId,supportResponseMaterialId=attempt.ItemId,
                sourceWorkAreaId=attempt.SourceWorkAreaId,targetWorkAreaId=attempt.TargetWorkAreaId,
                materialId=attempt.MaterialId,quantity=attempt.Quantity,approvedByUserId=attempt.ApproverId
            });
            using var response=await client.SendAsync(request,ct);
            if (!execute && response.StatusCode==HttpStatusCode.NotFound) return null;
            // A ProblemDetails response is not proof of a durable rejection. Only the operation result is authoritative.
            if (!response.IsSuccessStatusCode) throw Unavailable();
            var envelope=await response.Content.ReadFromJsonAsync<Envelope>(ct);
            var row=envelope?.Data ?? throw Unavailable();
            var decision=new SupportTransferDecision(row.OperationId,row.SupportResponseMaterialId,row.SourceWorkAreaId,row.TargetWorkAreaId,
                row.MaterialId,row.Quantity,row.ApprovedByUserId,row.Status,row.ErrorCode,row.DecidedAt);
            SupportMaterialApprovalProcessor.ValidateDecision(attempt,decision);
            return decision;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException or
            Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException) { throw Unavailable(); }
    }
    private static DomainRuleException Unavailable()=>new("RESOURCE_SERVICE_UNAVAILABLE",
        "The transfer decision is not yet confirmed. Its durable approval intent will be reconciled.",DomainErrorKind.ServiceUnavailable);
    private sealed record Envelope(SupportTransferOperationResult? Data);
}
