namespace Resource.Domain.Inventory;

/// <summary><c>material_transaction.transaction_type</c> (ERD v3.6).</summary>
public enum TransactionType
{
    OpeningStock,
    PurchaseDelivery,
    DonationReceipt,
    TaskReturn,
    TransferIn,
    TransferOut,
    AdjustmentDamaged,
    AdjustmentLost,
    AdjustmentDisposed
}

/// <summary><c>material_transaction.reference_type</c> (ERD v3.6): the source record a posting comes from.</summary>
public enum TransactionReferenceType
{
    PurchaseItem,
    DonationReceiptItem,
    TaskMaterialReportItem,
    SupportResponseMaterial,
    InventoryAdjustment
}

/// <summary>
/// One row of the append-only stock ledger (BR-23, BR-24): the only way usable stock changes. Quantity is
/// signed — inflows positive, outflows negative — and the sign is fixed by <see cref="Type"/>.
/// A source record (reference type + id + transaction type) is posted at most once (DB unique index).
/// </summary>
public sealed class MaterialTransaction
{
    private MaterialTransaction()
    {
    }

    public Guid MaterialTransactionId { get; private set; }

    public Guid MaterialId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public TransactionType Type { get; private set; }

    public decimal Quantity { get; private set; }

    public DateTimeOffset TransactionDate { get; private set; }

    public TransactionReferenceType? ReferenceType { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public string? Note { get; private set; }

    public static bool IsInflow(TransactionType type) => type is
        TransactionType.OpeningStock or
        TransactionType.PurchaseDelivery or
        TransactionType.DonationReceipt or
        TransactionType.TaskReturn or
        TransactionType.TransferIn;

    /// <summary>
    /// Posts a movement. <paramref name="absoluteQuantity"/> is always positive; the ledger stores it negative
    /// for outflows. Only OPENING_STOCK may be posted without a source reference.
    /// </summary>
    public static MaterialTransaction Post(
        Guid workAreaId,
        Guid materialId,
        TransactionType type,
        decimal absoluteQuantity,
        DateTimeOffset transactionDate,
        Guid? createdByUserId,
        string? note,
        TransactionReferenceType? referenceType = null,
        Guid? referenceId = null)
    {
        if (workAreaId == Guid.Empty || materialId == Guid.Empty)
        {
            throw DomainRuleException.Validation("TRANSACTION_INVALID", "Work area and material are required (BR-24).");
        }

        var quantity = Inventory.Quantity.Positive(absoluteQuantity, "TRANSACTION_QUANTITY_INVALID");

        if ((referenceType is null) != (referenceId is null))
        {
            throw DomainRuleException.Validation("TRANSACTION_REFERENCE_INVALID", "Reference type and id go together.");
        }

        if (referenceType is null && type != TransactionType.OpeningStock)
        {
            throw DomainRuleException.Validation(
                "TRANSACTION_REFERENCE_REQUIRED",
                "Every stock movement except opening stock must reference its source record.");
        }

        return new MaterialTransaction
        {
            MaterialTransactionId = Guid.NewGuid(),
            WorkAreaId = workAreaId,
            MaterialId = materialId,
            Type = type,
            Quantity = IsInflow(type) ? quantity : -quantity,
            TransactionDate = transactionDate.ToUniversalTime(),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            CreatedByUserId = createdByUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
    }
}
