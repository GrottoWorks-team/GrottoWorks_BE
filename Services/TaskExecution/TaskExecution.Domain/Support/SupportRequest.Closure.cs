namespace TaskExecution.Domain.Support;

public sealed partial class SupportRequest
{
    public Guid? ClosedBy { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? CloseReason { get; private set; }

    public bool Close(SupportRequestStatus target, string? reason, Guid actor, int? expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion is <= 0) throw DomainRuleException.Validation("VALIDATION_FAILED", "Version must be positive.");
        if (expectedVersion.HasValue && expectedVersion != Version)
            throw new DomainRuleException("SUPPORT_REQUEST_VERSION_MISMATCH", "The request changed.", DomainErrorKind.PreconditionFailed);
        if (target is not (SupportRequestStatus.Closed or SupportRequestStatus.Cancelled))
            throw DomainRuleException.Validation("SUPPORT_CLOSE_STATUS_INVALID", "Close status must be CLOSED or CANCELLED.");
        if (actor == Guid.Empty || reason?.Trim().Length > 500 || (target == SupportRequestStatus.Cancelled && string.IsNullOrWhiteSpace(reason)))
            throw DomainRuleException.Validation("SUPPORT_CLOSE_REASON_INVALID", "Cancellation requires a reason of 1–500 characters.");
        if (Status == target) return false;
        var offers = responses.SelectMany(response => response.Materials).ToList();
        if (offers.Any(item => item.ApprovalPending || item.Status == SupportMaterialStatus.Approved))
            throw new DomainRuleException("SUPPORT_TRANSFER_SYNC_PENDING", "Reconcile all withdrawals and receipts before closing the request.");
        if (target == SupportRequestStatus.Closed)
        {
            if (Status != SupportRequestStatus.Fulfilled)
                throw new DomainRuleException("SUPPORT_REQUEST_NOT_FULFILLED", "All staffing and usable-material requirements must be fulfilled before closing.");
        }
        else
        {
            if (Status is not (SupportRequestStatus.Planning or SupportRequestStatus.Published))
                throw new DomainRuleException("SUPPORT_REQUEST_INVALID_STATE", "Only planning or published requests can be cancelled.");
            if (offers.Any(item => item.Status == SupportMaterialStatus.Transferred) || responses.SelectMany(response => response.Members)
                .Any(member => member.Status is SupportMemberStatus.Approved or SupportMemberStatus.Completed))
                throw new DomainRuleException("SUPPORT_REQUEST_HAS_COMMITMENTS", "Resolve active assignments and transferred stock before cancelling; history cannot be reversed here.");
        }
        foreach (var response in responses)
        {
            foreach (var member in response.Members) member.CancelPending();
            foreach (var material in response.Materials) material.CancelPending();
            response.RefreshMemberStatus();
        }
        Status = target; CloseReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ClosedBy = actor; ClosedAt = now.ToUniversalTime(); UpdatedAt = ClosedAt; Version++; return true;
    }
}
