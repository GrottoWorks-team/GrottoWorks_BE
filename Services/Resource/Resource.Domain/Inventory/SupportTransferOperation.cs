namespace Resource.Domain.Inventory;

/// <summary>A terminal decision for one immutable approval attempt, including declined stock withdrawals.</summary>
public sealed class SupportTransferOperation
{
    private SupportTransferOperation() { }
    public Guid Id { get; private set; }
    public Guid SupportResponseMaterialId { get; private set; }
    public Guid SourceWorkAreaId { get; private set; }
    public Guid TargetWorkAreaId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid ApprovedByUserId { get; private set; }
    public string Status { get; private set; } = "COMPLETED";
    public string? ErrorCode { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }

    public static SupportTransferOperation Decide(Guid id, Guid itemId, Guid source, Guid target, Guid material,
        decimal quantity, Guid approver, string? rejection, DateTimeOffset now)
    {
        if (id == Guid.Empty || itemId == Guid.Empty || source == Guid.Empty || target == Guid.Empty ||
            material == Guid.Empty || approver == Guid.Empty || quantity <= 0 || quantity > 9999999999.99m || decimal.Round(quantity, 2) != quantity)
            throw DomainRuleException.Validation("SUPPORT_TRANSFER_OPERATION_INVALID", "Approval identities and a positive (12,2) quantity are required.");
        return new SupportTransferOperation
        {
            Id = id, SupportResponseMaterialId = itemId, SourceWorkAreaId = source, TargetWorkAreaId = target,
            MaterialId = material, Quantity = quantity, ApprovedByUserId = approver,
            Status = rejection is null ? "COMPLETED" : "REJECTED", ErrorCode = rejection, DecidedAt = now
        };
    }

    public void EnsureMatches(Guid itemId, Guid source, Guid target, Guid material, decimal quantity, Guid approver)
    {
        if (SupportResponseMaterialId != itemId || SourceWorkAreaId != source || TargetWorkAreaId != target ||
            MaterialId != material || Quantity != quantity || ApprovedByUserId != approver)
            throw new DomainRuleException("SUPPORT_TRANSFER_OPERATION_CONFLICT", "An approval attempt cannot be reused with different data.");
    }
}
