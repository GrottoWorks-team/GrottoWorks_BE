namespace TaskExecution.Domain.Support;

public enum SupportMaterialStatus { Pending,Approved,Rejected,Transferred,Cancelled }
public sealed record SupportMaterialOfferInput(Guid RequestMaterialId,Guid SourceWorkAreaId,decimal OfferedQuantity,string? Note=null);

public sealed class SupportResponseMaterial
{
    private SupportResponseMaterial() { }
    public Guid Id {get;private set;}
    public Guid ResponseId {get;private set;}
    public Guid RequestId {get;private set;}
    public Guid RequestMaterialId {get;private set;}
    public Guid SourceWorkAreaId {get;private set;}
    public Guid MaterialId {get;private set;}
    public decimal OfferedQuantity {get;private set;}
    public decimal? ApprovedQuantity {get;private set;}
    public decimal? ReceivedUsableQuantity {get;private set;}
    public decimal? ReceivedUnusableQuantity {get;private set;}
    public SupportMaterialStatus Status {get;private set;}=SupportMaterialStatus.Pending;
    public Guid? ApprovedBy {get;private set;}
    public DateTimeOffset? ApprovedAt {get;private set;}
    public Guid? ReceivedConfirmedBy {get;private set;}
    public DateTimeOffset? ReceivedConfirmedAt {get;private set;}
    public string? Note {get;private set;}
    public int Version {get;private set;}=1;
    public Guid? ApprovalOperationId { get; private set; }
    public decimal? ApprovalRequestedQuantity { get; private set; }
    public Guid? ApprovalRequestedBy { get; private set; }
    public DateTimeOffset? ApprovalRequestedAt { get; private set; }
    public string? ApprovalCorrelationId { get; private set; }
    public string? ApprovalErrorCode { get; private set; }
    public string? ReviewReason { get; private set; }
    public bool ApprovalPending => Status == SupportMaterialStatus.Pending && ApprovalOperationId.HasValue && ApprovalErrorCode is null;

    public void EnsureVersion(int? version)
    {
        if (version is <= 0) throw DomainRuleException.Validation("VALIDATION_FAILED", "Version must be positive.");
        if (version.HasValue && version != Version)
            throw new DomainRuleException("SUPPORT_MATERIAL_VERSION_MISMATCH", "The material offer changed.", DomainErrorKind.PreconditionFailed);
    }

    internal void BeginApproval(decimal quantity, Guid actor, string correlationId, DateTimeOffset now)
    {
        if (Status != SupportMaterialStatus.Pending)
            throw new DomainRuleException("SUPPORT_MATERIAL_INVALID_STATE", "Only a pending offer can be approved.");
        if (quantity <= 0 || quantity > OfferedQuantity || decimal.Round(quantity, 2) != quantity)
            throw DomainRuleException.Validation("SUPPORT_MATERIAL_QUANTITY_INVALID", "Approve a positive (12,2) quantity no greater than the offer.");
        if (ApprovalPending)
        {
            if (quantity != ApprovalRequestedQuantity)
                throw new DomainRuleException("SUPPORT_MATERIAL_APPROVAL_PENDING", "An immutable approval attempt is being reconciled.");
            return;
        }
        ApprovalOperationId = Guid.NewGuid(); ApprovalRequestedQuantity = quantity; ApprovalRequestedBy = actor;
        ApprovalRequestedAt = now; ApprovalCorrelationId = correlationId; ApprovalErrorCode = null; Version++;
    }

    internal bool ResolveApproval(Guid operationId, string? rejection, DateTimeOffset now)
    {
        if (ApprovalOperationId != operationId)
            throw new DomainRuleException("SUPPORT_MATERIAL_OPERATION_MISMATCH", "The approval attempt does not match this offer.");
        if (!ApprovalPending) return false;
        ApprovalErrorCode = rejection;
        if (rejection is null)
        {
            Status = SupportMaterialStatus.Approved; ApprovedQuantity = ApprovalRequestedQuantity;
            ApprovedBy = ApprovalRequestedBy; ApprovedAt = now;
        }
        Version++; return true;
    }

    internal void Reject(Guid actor, string? reason, DateTimeOffset now)
    {
        if (ApprovalPending) throw new DomainRuleException("SUPPORT_MATERIAL_APPROVAL_PENDING", "Reconcile the in-flight withdrawal before rejecting this offer.");
        if (Status != SupportMaterialStatus.Pending)
            throw new DomainRuleException("SUPPORT_MATERIAL_INVALID_STATE", "Only a pending offer can be rejected.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw DomainRuleException.Validation("SUPPORT_REJECTION_REASON_REQUIRED", "Rejection requires a reason of 1–500 characters.");
        Status = SupportMaterialStatus.Rejected; ApprovedBy = actor; ApprovedAt = now; ReviewReason = reason.Trim(); Version++;
    }

    internal bool Receive(decimal usable, decimal unusable, Guid officer, DateTimeOffset confirmedAt)
    {
        if (Status is not (SupportMaterialStatus.Approved or SupportMaterialStatus.Transferred) || ApprovedQuantity is null)
            throw new DomainRuleException("SUPPORT_TRANSFER_NOT_APPROVED", "Reconcile the withdrawal before applying its receipt.");
        if (usable < 0 || unusable < 0 || usable + unusable > ApprovedQuantity || decimal.Round(usable,2) != usable ||
            decimal.Round(unusable,2) != unusable || officer == Guid.Empty || confirmedAt == default)
            throw DomainRuleException.Validation("SUPPORT_TRANSFER_RESULT_INVALID", "The received quantities or confirmation identity are invalid.");
        if (Status == SupportMaterialStatus.Transferred)
        {
            if (ReceivedUsableQuantity != usable || ReceivedUnusableQuantity != unusable ||
                ReceivedConfirmedBy != officer || ReceivedConfirmedAt != confirmedAt)
                throw new DomainRuleException("SUPPORT_TRANSFER_RESULT_CONFLICT", "A confirmed receipt cannot be rewritten.");
            return false;
        }
        ReceivedUsableQuantity = usable; ReceivedUnusableQuantity = unusable;
        ReceivedConfirmedBy = officer; ReceivedConfirmedAt = confirmedAt.ToUniversalTime();
        Status = SupportMaterialStatus.Transferred; Version++; return true;
    }

    internal void CancelPending()
    {
        if (Status != SupportMaterialStatus.Pending) return;
        if (ApprovalPending) throw new DomainRuleException("SUPPORT_MATERIAL_APPROVAL_PENDING", "An approval operation is still in flight.");
        Status = SupportMaterialStatus.Cancelled; Version++;
    }

    internal static SupportResponseMaterial Offer(Guid responseId,Guid requestId,Guid materialId,SupportMaterialOfferInput input)
    {
        ValidateOffer(input);
        return new SupportResponseMaterial
        {
            Id=Guid.NewGuid(),ResponseId=responseId,RequestId=requestId,RequestMaterialId=input.RequestMaterialId,
            SourceWorkAreaId=input.SourceWorkAreaId,MaterialId=materialId,OfferedQuantity=input.OfferedQuantity,
            Note=string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim()
        };
    }

    internal static void ValidateOffer(SupportMaterialOfferInput input)
    {
        if(input.RequestMaterialId==Guid.Empty || input.SourceWorkAreaId==Guid.Empty || input.OfferedQuantity<=0 ||
            input.OfferedQuantity>9999999999.99m || decimal.Round(input.OfferedQuantity,2)!=input.OfferedQuantity || input.Note?.Length>2000)
            throw DomainRuleException.Validation("SUPPORT_MATERIAL_OFFER_INVALID","Offer requires valid identities, a positive (12,2) quantity and note up to 2000 characters.");
    }
}
