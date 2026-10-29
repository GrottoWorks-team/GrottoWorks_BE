using Resource.Domain.Inventory;

namespace Resource.Domain.Purchases;

/// <summary><c>purchase.purchase_status</c> (ERD v3.6.2, BA 9.5 "Mỗi lần mua").</summary>
public enum PurchaseStatus
{
    Submitted,
    NeedsInfo,
    Confirmed,
    Cancelled
}

/// <summary>One line the buyer bought against a purchase request line.</summary>
public sealed record PurchaseLineInput(Guid PurchaseRequestItemId, decimal PurchasedQuantity, decimal UnitPrice, decimal? TotalPrice);

/// <summary>What the officer counted on delivery for one purchase line.</summary>
public sealed record DeliveryLineInput(Guid PurchaseItemId, decimal ReceivedUsableQuantity, decimal ReceivedUnusableQuantity);

/// <summary>
/// <c>purchase</c> (ERD v3.6.2, F-RES-09/10/11): one buying trip of the volunteer holding the ACCEPTED assignment of
/// the PURCHASE task (BR-30). SUBMITTED never increases stock (BR-26); the officer confirms the usable quantity on
/// delivery, which is what reaches the ledger. Actual cost above the approved estimate needs the leader's approval
/// before confirmation (BR-32). An invalid receipt sends it to NEEDS_INFO without losing the record.
/// </summary>
public sealed class Purchase
{
    public const int MaxReceiptFiles = 5;
    public const int MaxSupplierLength = 150;
    public const int MaxReasonLength = 500;
    public const decimal MaxMoney = 999_999_999_999.99m;

    private readonly List<PurchaseItem> _items = [];

    private Purchase()
    {
    }

    public Guid PurchaseId { get; private set; }

    public Guid PurchaseRequestId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid BuyerUserId { get; private set; }

    public Guid? ConfirmedByUserId { get; private set; }

    public string? SupplierName { get; private set; }

    public DateOnly PurchaseDate { get; private set; }

    public decimal TotalCost { get; private set; }

    public PurchaseStatus Status { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? CostOverrunApprovedByUserId { get; private set; }

    public DateTimeOffset? CostOverrunApprovedAt { get; private set; }

    /// <summary>Receipt files (F-NTF-01 <c>file_object</c> ids). Technical column until NotificationAudit owns the link.</summary>
    public Guid[] ReceiptFileIds { get; private set; } = [];

    /// <summary>Why the officer asked for more information (technical column).</summary>
    public string? InfoRequestReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<PurchaseItem> Items => _items;

    /// <param name="requestLines">Lines of the purchase request: id → material.</param>
    public static Purchase Record(
        Guid purchaseRequestId,
        Guid taskId,
        Guid buyerUserId,
        IReadOnlyDictionary<Guid, Guid> requestLines,
        IReadOnlyCollection<PurchaseLineInput> lines,
        string? supplierName,
        DateOnly purchaseDate,
        IReadOnlyCollection<Guid> receiptFileIds,
        DateOnly today,
        DateTimeOffset now)
    {
        if (purchaseDate > today)
        {
            throw DomainRuleException.Validation("PURCHASE_DATE_IN_FUTURE", "The purchase date cannot be in the future.");
        }

        if (lines.Count == 0)
        {
            throw DomainRuleException.Validation("PURCHASE_ITEMS_REQUIRED", "Record at least one purchased line.");
        }

        if (lines.Select(line => line.PurchaseRequestItemId).Distinct().Count() != lines.Count)
        {
            throw DomainRuleException.Validation("PURCHASE_DUPLICATE_LINE", "Each request line can appear only once in a purchase.");
        }

        var supplier = string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim();
        if (supplier is { Length: > MaxSupplierLength })
        {
            throw DomainRuleException.Validation("PURCHASE_SUPPLIER_TOO_LONG", $"Supplier must not exceed {MaxSupplierLength} characters.");
        }

        var purchase = new Purchase
        {
            PurchaseId = Guid.NewGuid(),
            PurchaseRequestId = purchaseRequestId,
            TaskId = taskId,
            BuyerUserId = buyerUserId,
            SupplierName = supplier,
            PurchaseDate = purchaseDate,
            Status = PurchaseStatus.Submitted,
            CreatedAt = now.ToUniversalTime()
        };
        foreach (var line in lines)
        {
            if (!requestLines.TryGetValue(line.PurchaseRequestItemId, out var materialId))
            {
                throw DomainRuleException.Validation("PURCHASE_REQUEST_ITEM_UNKNOWN", $"Line {line.PurchaseRequestItemId} is not part of the purchase request.");
            }

            purchase._items.Add(PurchaseItem.Create(purchase.PurchaseId, materialId, line));
        }

        purchase.TotalCost = purchase._items.Sum(item => item.TotalPrice);
        purchase.SetReceipts(receiptFileIds);
        return purchase;
    }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "PURCHASE_VERSION_MISMATCH",
                "The purchase was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    /// <summary>The buyer adds receipt files; a purchase waiting for information goes back to the officer.</summary>
    public void AttachReceipts(IReadOnlyCollection<Guid> fileIds)
    {
        if (Status is not (PurchaseStatus.Submitted or PurchaseStatus.NeedsInfo))
        {
            throw new DomainRuleException("PURCHASE_INVALID_STATE", "Receipts can only be attached before the delivery is confirmed.");
        }

        if (fileIds.Count == 0)
        {
            throw DomainRuleException.Validation("PURCHASE_RECEIPT_REQUIRED", "Attach at least one receipt file.");
        }

        SetReceipts(ReceiptFileIds.Concat(fileIds).Distinct().ToList());
        Status = PurchaseStatus.Submitted;
        InfoRequestReason = null;
    }

    public void RequestInfo(string? reason)
    {
        EnsureSubmitted();
        var cleaned = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleaned is null || cleaned.Length > MaxReasonLength)
        {
            throw DomainRuleException.Validation("PURCHASE_INFO_REASON_REQUIRED", $"A reason of at most {MaxReasonLength} characters is required.");
        }

        Status = PurchaseStatus.NeedsInfo;
        InfoRequestReason = cleaned;
    }

    /// <summary>BR-32: only when the cost is actually above the approved estimate.</summary>
    public void ApproveCostOverrun(Guid leaderUserId, decimal remainingEstimate, DateTimeOffset now)
    {
        EnsureSubmitted();
        if (!ExceedsEstimate(remainingEstimate))
        {
            throw new DomainRuleException("PURCHASE_NO_COST_OVERRUN", "The actual cost is within the approved estimate.");
        }

        CostOverrunApprovedByUserId = leaderUserId;
        CostOverrunApprovedAt = now.ToUniversalTime();
    }

    public bool ExceedsEstimate(decimal? remainingEstimate) => remainingEstimate is { } estimate && TotalCost > estimate;

    /// <summary>
    /// F-RES-10: usable and unusable counted per line (usable + unusable ≤ purchased); blocked while an unapproved cost
    /// overrun exists (BR-32). The caller posts the usable quantities to the ledger.
    /// </summary>
    public void ConfirmDelivery(Guid officerUserId, IReadOnlyCollection<DeliveryLineInput> lines, decimal? remainingEstimate, DateTimeOffset now)
    {
        EnsureSubmitted();
        if (ExceedsEstimate(remainingEstimate) && CostOverrunApprovedByUserId is null)
        {
            throw new DomainRuleException(
                "PURCHASE_COST_OVERRUN_NOT_APPROVED",
                "The actual cost is above the approved estimate; the work-area leader must approve the overrun first (BR-32).");
        }

        if (lines.Count != _items.Count || lines.Select(line => line.PurchaseItemId).Distinct().Count() != _items.Count)
        {
            throw DomainRuleException.Validation("PURCHASE_DELIVERY_ITEMS_MISMATCH", "Confirm every purchased line exactly once.");
        }

        foreach (var line in lines)
        {
            var item = _items.FirstOrDefault(candidate => candidate.PurchaseItemId == line.PurchaseItemId)
                ?? throw DomainRuleException.Validation("PURCHASE_DELIVERY_ITEMS_MISMATCH", $"Line {line.PurchaseItemId} is not part of this purchase.");
            item.SetReceived(line.ReceivedUsableQuantity, line.ReceivedUnusableQuantity);
        }

        Status = PurchaseStatus.Confirmed;
        ConfirmedByUserId = officerUserId;
        ConfirmedAt = now.ToUniversalTime();
    }

    private void EnsureSubmitted()
    {
        if (Status != PurchaseStatus.Submitted)
        {
            throw new DomainRuleException("PURCHASE_INVALID_STATE", "Only a submitted purchase can be reviewed or confirmed.");
        }
    }

    private void SetReceipts(IReadOnlyCollection<Guid> fileIds)
    {
        if (fileIds.Count > MaxReceiptFiles)
        {
            throw DomainRuleException.Validation("PURCHASE_TOO_MANY_RECEIPTS", $"A purchase holds at most {MaxReceiptFiles} receipt files.");
        }

        ReceiptFileIds = fileIds.Distinct().ToArray();
    }
}

/// <summary><c>purchase_item</c> (ERD v3.6.2): quantity, prices and, after delivery, the usable / unusable count.</summary>
public sealed class PurchaseItem
{
    private PurchaseItem()
    {
    }

    public Guid PurchaseItemId { get; private set; }

    public Guid PurchaseId { get; private set; }

    public Guid PurchaseRequestItemId { get; private set; }

    public Guid MaterialId { get; private set; }

    public decimal PurchasedQuantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal TotalPrice { get; private set; }

    public decimal? ReceivedUsableQuantity { get; private set; }

    public decimal? ReceivedUnusableQuantity { get; private set; }

    internal static PurchaseItem Create(Guid purchaseId, Guid materialId, PurchaseLineInput line)
    {
        var quantity = Quantity.Positive(line.PurchasedQuantity, "PURCHASE_QUANTITY_INVALID", "Purchased quantity");
        var unitPrice = Money(line.UnitPrice, "Unit price");
        var total = line.TotalPrice is { } given ? Money(given, "Total price") : decimal.Round(quantity * unitPrice, 2);
        return new PurchaseItem
        {
            PurchaseItemId = Guid.NewGuid(),
            PurchaseId = purchaseId,
            PurchaseRequestItemId = line.PurchaseRequestItemId,
            MaterialId = materialId,
            PurchasedQuantity = quantity,
            UnitPrice = unitPrice,
            TotalPrice = total
        };
    }

    internal void SetReceived(decimal usable, decimal unusable)
    {
        if (usable < 0 || unusable < 0 || decimal.Round(usable, 2) != usable || decimal.Round(unusable, 2) != unusable ||
            usable + unusable > PurchasedQuantity)
        {
            throw DomainRuleException.Validation(
                "PURCHASE_DELIVERY_QUANTITY_INVALID",
                $"Usable and unusable must be 0 or more and together at most the {PurchasedQuantity} purchased.");
        }

        ReceivedUsableQuantity = usable;
        ReceivedUnusableQuantity = unusable;
    }

    private static decimal Money(decimal value, string field)
    {
        if (value < 0 || value > Purchase.MaxMoney || decimal.Round(value, 2) != value)
        {
            throw DomainRuleException.Validation("PURCHASE_PRICE_INVALID", $"{field} must be 0 or more with at most 2 decimals.");
        }

        return value;
    }
}
