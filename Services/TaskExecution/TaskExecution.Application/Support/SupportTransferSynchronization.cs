using System.Text.Json;
using Resource.Contracts.Events;
using Resource.Contracts.Inventory;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;

namespace TaskExecution.Application.Support;

public interface ISupportTransferReceiptLookup
{
    Task<SupportTransferSnapshot?> GetAsync(Guid itemId, string correlationId, CancellationToken ct);
}

public sealed class SupportTransferSynchronization(ISupportRequestRepository requests, ISupportMaterialSyncStore store,
    ISupportTransferReceiptLookup receipts, IScopeProjectionStore scopes, ITaskEventOutbox events, TimeProvider clock)
{
    public async Task ReconcileAsync(Guid itemId, CancellationToken ct)
    {
        var requestId = await FindRequestAsync(itemId, ct);
        var request = await requests.GetAsync(requestId, false, ct) ?? throw Unavailable();
        var item = request.GetMaterialOffer(itemId);
        if (item.Status != SupportMaterialStatus.Approved) return;
        var transfer = await receipts.GetAsync(itemId, item.ApprovalCorrelationId ?? "support-transfer-reconciliation", ct) ?? throw Unavailable();
        await ApplyAsync(transfer, item.ApprovalCorrelationId ?? "support-transfer-reconciliation", ct);
    }

    public async Task ApplyAsync(SupportTransferSnapshot transfer, string correlationId, CancellationToken ct)
    {
        var requestId = await FindRequestAsync(transfer.SupportResponseMaterialId, ct);
        await requests.InRequestTransactionAsync(requestId, async () =>
        {
            var request = await requests.GetAsync(requestId, true, ct) ?? throw Unavailable();
            var item = request.GetMaterialOffer(transfer.SupportResponseMaterialId);
            var area = await scopes.GetWorkAreaAsync(request.WorkAreaId, ct) ?? throw Unavailable();
            if (transfer.SourceWorkAreaId != item.SourceWorkAreaId || transfer.TargetWorkAreaId != request.WorkAreaId ||
                transfer.MaterialId != item.MaterialId || transfer.Quantity != item.ApprovedQuantity || transfer.ApprovedByUserId != item.ApprovedBy ||
                transfer.SeasonId != request.SeasonId || transfer.ParishId != area.ParishId || transfer.TransferredAt == default ||
                transfer.Status is not ("IN_TRANSIT" or "RECEIVED")) throw Unavailable();
            if (transfer.Status == "IN_TRANSIT")
            {
                if (transfer.ReceivedUsableQuantity is not null || transfer.ReceivedUnusableQuantity is not null ||
                    transfer.ReceivedConfirmedByUserId is not null || transfer.ReceivedConfirmedAt is not null) throw Unavailable();
                return false;
            }
            if (transfer.ReceivedUsableQuantity is not { } usable || transfer.ReceivedUnusableQuantity is not { } unusable ||
                transfer.ReceivedConfirmedByUserId is not { } officer || transfer.ReceivedConfirmedAt is not { } confirmedAt ||
                confirmedAt < transfer.TransferredAt) throw Unavailable();
            var now = clock.GetUtcNow();
            if (!request.ReceiveMaterial(item.Id, usable, unusable, officer, confirmedAt, now)) return false;
            events.Add(new TaskIntegrationEvent(Guid.NewGuid(), "support.transfer.synchronized", 1, now, correlationId, officer,
                area.ParishId, request.SeasonId, "support_request", request.Id, request.Version, JsonSerializer.SerializeToElement(new
                {
                    supportRequestId = request.Id, supportResponseMaterialId = item.Id, workAreaId = request.WorkAreaId,
                    sourceWorkAreaId = item.SourceWorkAreaId, materialId = item.MaterialId, receivedUsableQuantity = usable,
                    receivedUnusableQuantity = unusable, confirmedByUserId = officer,
                    status = JsonNamingPolicy.SnakeCaseUpper.ConvertName(request.Status.ToString())
                })));
            await requests.SaveAsync(ct); return true;
        }, ct);
    }
    private async Task<Guid> FindRequestAsync(Guid itemId, CancellationToken ct) =>
        await store.FindRequestIdAsync(itemId, ct) ?? throw Unavailable();
    private static DomainRuleException Unavailable() => new("SUPPORT_TRANSFER_SYNC_PENDING",
        "The authoritative transfer cannot yet be reconciled with its local approval. It will be retried.", DomainErrorKind.ServiceUnavailable);
}

public sealed class SupportTransferReceiptIngestion(IScopeEventInbox inbox, ISupportMaterialSyncStore store,
    ISupportRequestRepository requests, SupportMaterialApprovalProcessor approvals, SupportTransferSynchronization synchronization)
{
    public async Task HandleAsync(ResourceIntegrationEvent message, CancellationToken ct)
    {
        if (message.EventType != SupportTransferConfirmed.EventType) return;
        SupportTransferConfirmed receipt;
        try { receipt = message.Payload.Deserialize<SupportTransferConfirmed>(JsonSerializerOptions.Web) ?? throw new FormatException("Missing transfer receipt."); }
        catch (JsonException exception) { throw new FormatException("Invalid transfer receipt.", exception); }
        if (message.EventId == Guid.Empty || message.AggregateId != receipt.SupportResponseMaterialId ||
            message.Version != 1 || message.AggregateType != "support_transfer" || message.OccurredAt == default ||
            receipt.SupportResponseMaterialId == Guid.Empty || receipt.ParishId == Guid.Empty || receipt.SourceWorkAreaId == Guid.Empty ||
            receipt.TargetWorkAreaId == Guid.Empty || receipt.MaterialId == Guid.Empty || receipt.ConfirmedByUserId == Guid.Empty ||
            receipt.SentQuantity <= 0 || receipt.ReceivedUsableQuantity < 0 || receipt.ReceivedUnusableQuantity < 0 ||
            receipt.ReceivedUsableQuantity + receipt.ReceivedUnusableQuantity > receipt.SentQuantity || receipt.ConfirmedAt == default ||
            message.ParishId != receipt.ParishId || (message.ActorId.HasValue && message.ActorId != receipt.ConfirmedByUserId))
            throw new FormatException("Invalid transfer receipt identity or quantities.");
        await inbox.ExecuteAsync("support-transfer-receipt", message.EventId, async () =>
        {
            // A receipt can arrive before the HTTP approval response. Recover that durable outcome first.
            await approvals.ProcessAsync(receipt.SupportResponseMaterialId, ct);
            var requestId = await store.FindRequestIdAsync(receipt.SupportResponseMaterialId, ct)
                ?? throw new DomainRuleException("SUPPORT_TRANSFER_SYNC_PENDING", "The support offer is not yet available.", DomainErrorKind.ServiceUnavailable);
            var request = await requests.GetAsync(requestId, false, ct)
                ?? throw new DomainRuleException("SUPPORT_TRANSFER_SYNC_PENDING", "The request is not yet available.", DomainErrorKind.ServiceUnavailable);
            var item = request.GetMaterialOffer(receipt.SupportResponseMaterialId);
            if (message.SeasonId.HasValue && message.SeasonId != request.SeasonId) throw new FormatException("Transfer season does not match.");
            if (receipt.SourceWorkAreaId != item.SourceWorkAreaId || receipt.TargetWorkAreaId != request.WorkAreaId ||
                receipt.MaterialId != item.MaterialId || receipt.SentQuantity != item.ApprovedQuantity)
                throw new FormatException("Transfer event does not match its approved offer.");
            // Use the same authoritative lookup as recovery; do not fabricate transfer/audit timestamps omitted by the event.
            await synchronization.ReconcileAsync(receipt.SupportResponseMaterialId, ct);
            var reconciled = await requests.GetAsync(requestId, false, ct)
                ?? throw new DomainRuleException("SUPPORT_TRANSFER_SYNC_PENDING", "The request is not yet available.", DomainErrorKind.ServiceUnavailable);
            var received = reconciled.GetMaterialOffer(receipt.SupportResponseMaterialId);
            if (received.Status != SupportMaterialStatus.Transferred)
                throw new DomainRuleException("SUPPORT_TRANSFER_SYNC_PENDING", "Resource has not yet returned the receipt snapshot.", DomainErrorKind.ServiceUnavailable);
            if (received.ReceivedUsableQuantity != receipt.ReceivedUsableQuantity || received.ReceivedUnusableQuantity != receipt.ReceivedUnusableQuantity ||
                received.ReceivedConfirmedBy != receipt.ConfirmedByUserId || received.ReceivedConfirmedAt != receipt.ConfirmedAt)
                throw new FormatException("Transfer event conflicts with the authoritative receipt.");
        }, ct);
    }
}
