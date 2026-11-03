namespace TaskExecution.Domain.Support;

public sealed partial class SupportRequest
{
    public SupportResponseMaterial GetMaterialOffer(Guid itemId) => responses.SelectMany(response => response.Materials)
        .SingleOrDefault(item => item.Id == itemId)
        ?? throw DomainRuleException.NotFound("SUPPORT_MATERIAL_NOT_FOUND", "The material offer does not exist.");

    public SupportResponseMaterial BeginMaterialApproval(Guid itemId, decimal quantity, Guid actor, int? version, string correlation, DateTimeOffset now)
    {
        EnsureMaterialReviewAllowed();
        var item = GetMaterialOffer(itemId); item.EnsureVersion(version);
        var wasPending = item.ApprovalPending;
        item.BeginApproval(quantity, actor, correlation, now);
        if (!wasPending) MaterialChanged(item, now);
        return item;
    }

    public SupportResponseMaterial RejectMaterial(Guid itemId, Guid actor, string? reason, int? version, DateTimeOffset now)
    {
        EnsureMaterialReviewAllowed();
        var item = GetMaterialOffer(itemId); item.EnsureVersion(version); item.Reject(actor, reason, now);
        MaterialChanged(item, now); return item;
    }

    public bool ResolveMaterialApproval(Guid itemId, Guid operationId, string? rejection, DateTimeOffset now)
    {
        var item = GetMaterialOffer(itemId);
        if (!item.ResolveApproval(operationId, rejection, now)) return false;
        MaterialChanged(item, now); return true;
    }

    private void EnsureMaterialReviewAllowed()
    {
        if (Status is not (SupportRequestStatus.Published or SupportRequestStatus.PartiallyFulfilled or SupportRequestStatus.Fulfilled))
            throw new DomainRuleException("SUPPORT_REQUEST_INVALID_STATE", "The support request is not open for review.");
    }

    private void MaterialChanged(SupportResponseMaterial item, DateTimeOffset now)
    {
        responses.Single(response => response.Id == item.ResponseId).RefreshMemberStatus();
        if (Status is not (SupportRequestStatus.Closed or SupportRequestStatus.Cancelled)) RefreshStaffingFulfillment();
        UpdatedAt = now; Version++;
    }
}
