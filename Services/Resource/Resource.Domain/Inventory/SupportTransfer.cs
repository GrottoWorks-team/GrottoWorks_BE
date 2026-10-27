namespace Resource.Domain.Inventory;

public enum SupportTransferStatus
{
    /// <summary>TRANSFER_OUT posted on the source work area; waiting for the receiving officer.</summary>
    InTransit,

    /// <summary>The receiving officer confirmed; TRANSFER_IN posted for the usable part.</summary>
    Received
}

/// <summary>
/// <c>support_transfer</c> (technical, Resource side of D-03 / F-RES-20): the stock movement behind one approved
/// <c>support_response_material</c> (owned by TaskExecution, keyed by its id). Approval posts TRANSFER_OUT on the source
/// work area; one officer of the receiving work area confirms what arrived and only the usable part is posted as
/// TRANSFER_IN (BR-64). Both ledger rows reference <c>SUPPORT_RESPONSE_MATERIAL</c> = this id, so each posts once.
/// </summary>
public sealed class SupportTransfer
{
    public const int MaxNoteLength = 2000;

    private SupportTransfer()
    {
    }

    /// <summary>= <c>support_response_material_id</c> of TaskExecution (idempotency key of the internal call).</summary>
    public Guid SupportResponseMaterialId { get; private set; }

    public Guid SourceWorkAreaId { get; private set; }

    public Guid TargetWorkAreaId { get; private set; }

    public Guid MaterialId { get; private set; }

    public decimal Quantity { get; private set; }

    public SupportTransferStatus Status { get; private set; }

    public Guid ApprovedByUserId { get; private set; }

    public DateTimeOffset TransferredAt { get; private set; }

    public decimal? ReceivedUsableQuantity { get; private set; }

    public decimal? ReceivedUnusableQuantity { get; private set; }

    public Guid? ReceivedConfirmedByUserId { get; private set; }

    public DateTimeOffset? ReceivedConfirmedAt { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public static SupportTransfer Start(
        Guid supportResponseMaterialId,
        Guid sourceWorkAreaId,
        Guid targetWorkAreaId,
        Guid materialId,
        decimal quantity,
        Guid approvedByUserId,
        DateTimeOffset now)
    {
        if (supportResponseMaterialId == Guid.Empty || materialId == Guid.Empty)
        {
            throw DomainRuleException.Validation("SUPPORT_TRANSFER_INVALID", "Support response material and material are required.");
        }

        if (sourceWorkAreaId == targetWorkAreaId)
        {
            throw DomainRuleException.Validation("SUPPORT_TRANSFER_SAME_WORK_AREA", "Source and target work areas must be different.");
        }

        return new SupportTransfer
        {
            SupportResponseMaterialId = supportResponseMaterialId,
            SourceWorkAreaId = sourceWorkAreaId,
            TargetWorkAreaId = targetWorkAreaId,
            MaterialId = materialId,
            Quantity = Inventory.Quantity.Positive(quantity, "SUPPORT_TRANSFER_QUANTITY_INVALID", "Transfer quantity"),
            Status = SupportTransferStatus.InTransit,
            ApprovedByUserId = approvedByUserId,
            TransferredAt = now.ToUniversalTime()
        };
    }

    /// <summary>True when a retried call describes the same transfer (idempotent replay).</summary>
    public bool Matches(Guid sourceWorkAreaId, Guid targetWorkAreaId, Guid materialId, decimal quantity) =>
        SourceWorkAreaId == sourceWorkAreaId && TargetWorkAreaId == targetWorkAreaId && MaterialId == materialId && Quantity == quantity;

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "SUPPORT_TRANSFER_VERSION_MISMATCH",
                "The transfer was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    /// <summary>One receiving officer confirms (no second confirmation, R-23); usable + unusable ≤ sent.</summary>
    public void ConfirmReceipt(decimal usable, decimal unusable, string? note, Guid officerUserId, DateTimeOffset now)
    {
        if (Status != SupportTransferStatus.InTransit)
        {
            throw new DomainRuleException("SUPPORT_TRANSFER_ALREADY_RECEIVED", "The receipt of this transfer was already confirmed.");
        }

        if (usable < 0 || unusable < 0 || decimal.Round(usable, 2) != usable || decimal.Round(unusable, 2) != unusable ||
            usable + unusable > Quantity)
        {
            throw DomainRuleException.Validation(
                "SUPPORT_TRANSFER_RECEIPT_INVALID",
                $"Usable and unusable quantities must be 0 or more and together at most the {Quantity} sent.");
        }

        var cleaned = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleaned is { Length: > MaxNoteLength })
        {
            throw DomainRuleException.Validation("SUPPORT_TRANSFER_NOTE_TOO_LONG", $"Note must not exceed {MaxNoteLength} characters.");
        }

        Status = SupportTransferStatus.Received;
        ReceivedUsableQuantity = usable;
        ReceivedUnusableQuantity = unusable;
        ReceivedConfirmedByUserId = officerUserId;
        ReceivedConfirmedAt = now.ToUniversalTime();
        Note = cleaned;
    }
}
