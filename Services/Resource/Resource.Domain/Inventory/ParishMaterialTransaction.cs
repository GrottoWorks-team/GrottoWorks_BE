namespace Resource.Domain.Inventory;

/// <summary>
/// Append-only stock ledger of the parish warehouse (<c>parish_material_transaction</c>). Kept apart from the
/// work-area ledger so a material officer can never read or change parish stock by accident: the warehouse is
/// owned by the PARISH role and scoped by <see cref="ParishId"/> + <see cref="SeasonId"/>.
/// Quantity is signed — inflows positive, outflows negative — and the sign is fixed by <see cref="Type"/>.
/// </summary>
public sealed class ParishMaterialTransaction
{
    private ParishMaterialTransaction()
    {
    }

    public Guid ParishMaterialTransactionId { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid MaterialId { get; private set; }

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
        TransactionType.DistributionReturn;

    /// <summary>Only movements a parish warehouse can produce (no allocation, task return or support transfer).</summary>
    public static bool IsParishType(TransactionType type) => type is
        TransactionType.OpeningStock or
        TransactionType.PurchaseDelivery or
        TransactionType.DonationReceipt or
        TransactionType.AdjustmentDamaged or
        TransactionType.AdjustmentLost or
        TransactionType.AdjustmentDisposed or
        TransactionType.DistributionOut or
        TransactionType.DistributionReturn;

    /// <summary>
    /// Posts a movement. <paramref name="absoluteQuantity"/> is always positive; the ledger stores it negative for
    /// outflows. Only OPENING_STOCK may be posted without a source reference.
    /// </summary>
    public static ParishMaterialTransaction Post(
        Guid parishId,
        Guid seasonId,
        Guid materialId,
        TransactionType type,
        decimal absoluteQuantity,
        DateTimeOffset transactionDate,
        Guid? createdByUserId,
        string? note,
        TransactionReferenceType? referenceType = null,
        Guid? referenceId = null)
    {
        if (parishId == Guid.Empty || seasonId == Guid.Empty || materialId == Guid.Empty)
        {
            throw DomainRuleException.Validation("PARISH_TRANSACTION_INVALID", "Parish, season and material are required.");
        }

        if (!IsParishType(type))
        {
            throw DomainRuleException.Validation(
                "PARISH_TRANSACTION_TYPE_INVALID",
                $"'{type}' is not a parish warehouse movement.");
        }

        var quantity = Inventory.Quantity.Positive(absoluteQuantity, "PARISH_TRANSACTION_QUANTITY_INVALID");

        if ((referenceType is null) != (referenceId is null))
        {
            throw DomainRuleException.Validation("PARISH_TRANSACTION_REFERENCE_INVALID", "Reference type and id go together.");
        }

        if (referenceType is null && type != TransactionType.OpeningStock)
        {
            throw DomainRuleException.Validation(
                "PARISH_TRANSACTION_REFERENCE_REQUIRED",
                "Every parish warehouse movement except opening stock must reference its source record.");
        }

        return new ParishMaterialTransaction
        {
            ParishMaterialTransactionId = Guid.NewGuid(),
            ParishId = parishId,
            SeasonId = seasonId,
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
