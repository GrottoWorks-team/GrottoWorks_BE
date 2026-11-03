using System.Text.Json;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;

namespace TaskExecution.Application.Support;

public sealed record SupportTransferAttempt(Guid OperationId, Guid ItemId, Guid SourceWorkAreaId, Guid TargetWorkAreaId,
    Guid MaterialId, decimal Quantity, Guid ApproverId, string CorrelationId);
public sealed record SupportTransferDecision(Guid OperationId, Guid ItemId, Guid SourceWorkAreaId, Guid TargetWorkAreaId,
    Guid MaterialId, decimal Quantity, Guid ApproverId, string Status, string? ErrorCode, DateTimeOffset DecidedAt);

public interface ISupportTransferGateway
{
    Task<SupportTransferDecision?> FindDecisionAsync(SupportTransferAttempt attempt, CancellationToken ct);
    Task<SupportTransferDecision> ExecuteAsync(SupportTransferAttempt attempt, CancellationToken ct);
}
public interface ISupportMaterialSyncStore
{
    Task<Guid?> FindRequestIdAsync(Guid itemId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetPendingItemsAsync(Guid? after, int size, CancellationToken ct);
}

public sealed class SupportMaterialReviewEvents(ITaskEventOutbox outbox)
{
    public void Record(SupportRequest request, SupportResponseMaterial item, Guid parishId, Guid actor, string correlation, DateTimeOffset now)
    {
        var submitter = request.Responses.Single(response => response.Id == item.ResponseId).SubmittedBy;
        var eventType = item.Status == SupportMaterialStatus.Approved ? TaskEventTypes.SupportMaterialApproved : TaskEventTypes.SupportMaterialReviewed;
        outbox.Add(new TaskIntegrationEvent(Guid.NewGuid(), eventType, 1, now, correlation, actor, parishId,
            request.SeasonId, "support_request", request.Id, request.Version, JsonSerializer.SerializeToElement(new
            {
                supportRequestId = request.Id, supportResponseMaterialId = item.Id, workAreaId = request.WorkAreaId,
                sourceWorkAreaId = item.SourceWorkAreaId, materialId = item.MaterialId, materialOfficerUserId = submitter,
                approvalOperationId = item.ApprovalOperationId, approvedQuantity = item.ApprovedQuantity,
                status = item.Status.ToString().ToUpperInvariant(), errorCode = item.ApprovalErrorCode, reason = item.ReviewReason
            })));
    }
}

/// <summary>The caller's authorization and immutable intent commit before any remote stock command.</summary>
public sealed class ReviewSupportMaterialHandler(ISupportRequestRepository requests, ISupportMaterialSyncStore store,
    TaskScopeAccess scopes, ActiveUserAccess users, ITaskCorrelationContext correlation, TimeProvider clock,
    SupportMaterialApprovalProcessor processor, SupportMaterialReviewEvents events)
{
    public async Task<SupportResponseMaterialDto> HandleAsync(Guid itemId, bool approve, decimal? quantity, string? reason, int? version, CancellationToken ct = default)
    {
        var requestId = await store.FindRequestIdAsync(itemId, ct)
            ?? throw DomainRuleException.NotFound("SUPPORT_MATERIAL_NOT_FOUND", "The material offer does not exist.");
        var result = await requests.InRequestTransactionAsync(requestId, async () =>
        {
            var request = await requests.GetAsync(requestId, true, ct)
                ?? throw DomainRuleException.NotFound("SUPPORT_REQUEST_NOT_FOUND", "The support request does not exist.");
            var (area, _) = await scopes.GetWorkAreaForLeaderWriteAsync(request.WorkAreaId, ct);
            var actor = await users.EnsureCallerAsync(area.ParishId, ct);
            SupportResponseMaterial item;
            if (approve)
            {
                if (quantity is null) throw DomainRuleException.Validation("VALIDATION_FAILED", "Approved quantity is required.");
                item = request.BeginMaterialApproval(itemId, quantity.Value, actor.UserId, version, correlation.CorrelationId, clock.GetUtcNow());
            }
            else
            {
                item = request.RejectMaterial(itemId, actor.UserId, reason, version, clock.GetUtcNow());
                events.Record(request, item, area.ParishId, actor.UserId, correlation.CorrelationId, clock.GetUtcNow());
            }
            await requests.SaveAsync(ct);
            return SupportResponseMaterialDto.From(item);
        }, ct);
        if (!approve) return result;

        // The processor uses a fresh tracked request after the intent's transaction has committed.
        var decision = await processor.ProcessAsync(itemId, ct);
        if (decision.ErrorCode is not null)
            throw new DomainRuleException(decision.ErrorCode, "Resource declined this approval attempt; no stock was withdrawn by it.");
        return decision.Item;
    }
}

public sealed record SupportMaterialApprovalResult(SupportResponseMaterialDto Item, string? ErrorCode);
public sealed class SupportMaterialApprovalProcessor(ISupportRequestRepository requests, ISupportMaterialSyncStore store,
    ISupportTransferGateway gateway, IScopeProjectionStore scopes, SupportMaterialReviewEvents events, TimeProvider clock)
{
    public async Task<SupportMaterialApprovalResult> ProcessAsync(Guid itemId, CancellationToken ct)
    {
        var requestId = await store.FindRequestIdAsync(itemId, ct)
            ?? throw DomainRuleException.NotFound("SUPPORT_MATERIAL_NOT_FOUND", "The material offer does not exist.");
        return await requests.InRequestTransactionAsync(requestId, async () =>
        {
            var request = await requests.GetAsync(requestId, true, ct)
                ?? throw DomainRuleException.NotFound("SUPPORT_REQUEST_NOT_FOUND", "The support request does not exist.");
            var item = request.GetMaterialOffer(itemId);
            if (!item.ApprovalPending) return new SupportMaterialApprovalResult(SupportResponseMaterialDto.From(item), item.ApprovalErrorCode);
            var area = await scopes.GetWorkAreaAsync(request.WorkAreaId, ct)
                ?? throw DomainRuleException.NotFound("WORK_AREA_NOT_FOUND", "The target scope is missing.");
            var attempt = new SupportTransferAttempt(item.ApprovalOperationId!.Value, item.Id, item.SourceWorkAreaId, request.WorkAreaId,
                item.MaterialId, item.ApprovalRequestedQuantity!.Value, item.ApprovalRequestedBy!.Value, item.ApprovalCorrelationId!);
            var decision = await gateway.FindDecisionAsync(attempt, ct) ?? await gateway.ExecuteAsync(attempt, ct);
            ValidateDecision(attempt, decision);
            if (request.ResolveMaterialApproval(item.Id, decision.OperationId, decision.ErrorCode, decision.DecidedAt))
                events.Record(request, item, area.ParishId, attempt.ApproverId, attempt.CorrelationId, clock.GetUtcNow());
            await requests.SaveAsync(ct);
            return new SupportMaterialApprovalResult(SupportResponseMaterialDto.From(item), decision.ErrorCode);
        }, ct);
    }

    public static void ValidateDecision(SupportTransferAttempt attempt, SupportTransferDecision decision)
    {
        if (decision.OperationId != attempt.OperationId || decision.ItemId != attempt.ItemId || decision.SourceWorkAreaId != attempt.SourceWorkAreaId ||
            decision.TargetWorkAreaId != attempt.TargetWorkAreaId || decision.MaterialId != attempt.MaterialId || decision.Quantity != attempt.Quantity ||
            decision.ApproverId != attempt.ApproverId || decision.DecidedAt == default || decision.Status is not ("COMPLETED" or "REJECTED") ||
            (decision.Status == "REJECTED") != !string.IsNullOrWhiteSpace(decision.ErrorCode))
            throw new DomainRuleException("RESOURCE_SERVICE_UNAVAILABLE", "Resource returned an inconsistent approval decision.", DomainErrorKind.ServiceUnavailable);
    }
}
