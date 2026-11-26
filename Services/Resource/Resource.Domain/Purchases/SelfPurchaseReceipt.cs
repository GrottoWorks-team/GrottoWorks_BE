namespace Resource.Domain.Purchases;

/// <summary>One line of a self-purchase receipt: an existing catalog material and what it cost.</summary>
public sealed record SelfPurchaseLineInput(Guid MaterialId, decimal Quantity, decimal UnitPrice);

/// <summary>
/// <c>self_purchase_receipt</c> + <c>self_purchase_item</c> (26/11/2026): materials that someone bought on their own,
/// outside the purchase-request/purchase-task flow (e.g. a parishioner handed over goods with an invoice). The material
/// officer of the work area records it, the invoice photo is mandatory, and the usable quantity enters the work-area
/// ledger as <see cref="Inventory.TransactionType.PurchaseDelivery"/> so stock and cost stay traceable in one place.
/// This is deliberately a different table from <c>purchase</c> (which always needs an approved request) and from
/// <c>donation</c> (free contribution): money transparency needs the three channels apart.
/// </summary>
public sealed class SelfPurchaseReceipt
{
    public const int MaxInvoiceFiles = 5;
    public const int MaxBuyerNameLength = 150;
    public const int MaxSupplierLength = 150;
    public const int MaxNoteLength = 500;

    /// <summary>Recorded by the material officer for goods someone else bought (normal top-up path).</summary>
    public const string SourceMaterialOfficer = "MO_SELF_PURCHASE";

    /// <summary>Recorded by a volunteer during a task when materials ran out (emergency, below the limit).</summary>
    public const string SourceVolunteerEmergency = "VOLUNTEER_EMERGENCY";

    /// <summary>Emergency purchases during a task must stay under 500,000 VND; larger shortages go through the parish.</summary>
    public const decimal EmergencyPurchaseLimit = 500_000m;

    private readonly List<SelfPurchaseItem> _items = [];

    private SelfPurchaseReceipt()
    {
    }

    public Guid SelfPurchaseReceiptId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid SeasonId { get; private set; }

    /// <summary>The task whose execution ran out of materials (emergency purchases only).</summary>
    public Guid? TaskId { get; private set; }

    /// <summary><see cref="SourceMaterialOfficer"/> or <see cref="SourceVolunteerEmergency"/>.</summary>
    public string Source { get; private set; } = SourceMaterialOfficer;

    /// <summary>Material officer who entered the receipt.</summary>
    public Guid RecordedByUserId { get; private set; }

    /// <summary>Who actually paid for the goods (volunteer, leader, parishioner…). Not a system account.</summary>
    public string BuyerName { get; private set; } = string.Empty;

    public string? SupplierName { get; private set; }

    public DateOnly PurchaseDate { get; private set; }

    public decimal TotalCost { get; private set; }

    public Guid[] InvoiceFileIds { get; private set; } = [];

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<SelfPurchaseItem> Items => _items;

    public static SelfPurchaseReceipt Record(
        Guid workAreaId,
        Guid parishId,
        Guid seasonId,
        Guid recordedByUserId,
        string? buyerName,
        string? supplierName,
        DateOnly purchaseDate,
        IReadOnlyCollection<SelfPurchaseLineInput> lines,
        IReadOnlyCollection<Guid> invoiceFileIds,
        string? note,
        DateOnly today,
        DateTimeOffset now,
        Guid? taskId = null,
        string source = SourceMaterialOfficer)
    {
        if (purchaseDate > today)
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_DATE_IN_FUTURE", "The purchase date cannot be in the future.");
        }

        var buyer = buyerName?.Trim();
        if (string.IsNullOrEmpty(buyer) || buyer.Length > MaxBuyerNameLength)
        {
            throw DomainRuleException.Validation(
                "SELF_PURCHASE_BUYER_REQUIRED",
                $"Name of the person who paid is required and must not exceed {MaxBuyerNameLength} characters.");
        }

        var supplier = string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim();
        if (supplier is { Length: > MaxSupplierLength })
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_SUPPLIER_TOO_LONG", $"Supplier must not exceed {MaxSupplierLength} characters.");
        }

        if (lines.Count == 0)
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_ITEMS_REQUIRED", "Record at least one material line.");
        }

        if (lines.Select(line => line.MaterialId).Distinct().Count() != lines.Count)
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_DUPLICATE_LINE", "Each material can appear only once in a receipt.");
        }

        if (invoiceFileIds.Count == 0)
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_INVOICE_REQUIRED", "Attach at least one invoice photo.");
        }

        var cleanedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleanedNote is { Length: > MaxNoteLength })
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_NOTE_TOO_LONG", $"Note must not exceed {MaxNoteLength} characters.");
        }

        if (source is not (SourceMaterialOfficer or SourceVolunteerEmergency))
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_SOURCE_INVALID", "Unknown self-purchase source.");
        }

        var receipt = new SelfPurchaseReceipt
        {
            SelfPurchaseReceiptId = Guid.NewGuid(),
            WorkAreaId = workAreaId,
            ParishId = parishId,
            SeasonId = seasonId,
            TaskId = taskId,
            Source = source,
            RecordedByUserId = recordedByUserId,
            BuyerName = buyer,
            SupplierName = supplier,
            PurchaseDate = purchaseDate,
            InvoiceFileIds = invoiceFileIds.Distinct().Take(MaxInvoiceFiles).ToArray(),
            Note = cleanedNote,
            CreatedAt = now.ToUniversalTime()
        };

        foreach (var line in lines)
        {
            receipt._items.Add(SelfPurchaseItem.Create(receipt.SelfPurchaseReceiptId, line));
        }

        receipt.TotalCost = receipt._items.Sum(item => item.TotalPrice);
        if (string.Equals(receipt.Source, SourceVolunteerEmergency, StringComparison.Ordinal) &&
            receipt.TotalCost >= EmergencyPurchaseLimit)
        {
            throw DomainRuleException.Validation(
                "EMERGENCY_PURCHASE_LIMIT",
                $"An emergency purchase during a task must stay under {EmergencyPurchaseLimit:N0} VND; larger shortages go through the parish supply request.");
        }

        return receipt;
    }
}

/// <summary><c>self_purchase_item</c>: quantity and unit price of one material of a self-purchase receipt.</summary>
public sealed class SelfPurchaseItem
{
    private SelfPurchaseItem()
    {
    }

    public Guid SelfPurchaseItemId { get; private set; }

    public Guid SelfPurchaseReceiptId { get; private set; }

    public Guid MaterialId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal TotalPrice { get; private set; }

    internal static SelfPurchaseItem Create(Guid receiptId, SelfPurchaseLineInput line)
    {
        if (line.MaterialId == Guid.Empty)
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_MATERIAL_REQUIRED", "Each line needs a material.");
        }

        if (line.Quantity <= 0)
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_QUANTITY_INVALID", "Quantity must be greater than zero.");
        }

        if (line.UnitPrice < 0)
        {
            throw DomainRuleException.Validation("SELF_PURCHASE_PRICE_INVALID", "Unit price cannot be negative.");
        }

        return new SelfPurchaseItem
        {
            SelfPurchaseItemId = Guid.NewGuid(),
            SelfPurchaseReceiptId = receiptId,
            MaterialId = line.MaterialId,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            TotalPrice = Math.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero)
        };
    }
}
