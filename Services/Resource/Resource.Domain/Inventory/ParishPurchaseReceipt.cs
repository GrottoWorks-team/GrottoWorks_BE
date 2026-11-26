namespace Resource.Domain.Inventory;

/// <summary>One material line of a parish purchase receipt.</summary>
public sealed record ParishPurchaseLineInput(Guid MaterialId, decimal Quantity, decimal UnitPrice);

/// <summary>
/// <c>parish_purchase_receipt</c> (26/11/2026): the parish committee bought materials for the central warehouse.
/// The invoice photo is mandatory (money control), the total is computed server-side, and each line posts a
/// <c>PurchaseDelivery</c> row into the parish ledger so the stock can then be distributed down to work areas.
/// </summary>
public sealed class ParishPurchaseReceipt
{
    public const int MaxBuyerNameLength = 150;
    public const int MaxSupplierLength = 150;
    public const int MaxNoteLength = 500;
    public const int MaxInvoiceFiles = 10;

    private readonly List<ParishPurchaseItem> _items = [];

    private ParishPurchaseReceipt()
    {
    }

    public static ParishPurchaseReceipt Record(
        Guid parishId,
        Guid seasonId,
        Guid recordedByUserId,
        string? buyerName,
        string? supplierName,
        DateOnly purchaseDate,
        IReadOnlyCollection<ParishPurchaseLineInput> lines,
        IReadOnlyCollection<Guid> invoiceFileIds,
        string? note,
        DateOnly today,
        DateTimeOffset now)
    {
        if (parishId == Guid.Empty || seasonId == Guid.Empty)
        {
            throw DomainRuleException.Validation("PARISH_PURCHASE_INVALID", "Parish and season are required.");
        }

        if (purchaseDate > today)
        {
            throw DomainRuleException.Validation("PARISH_PURCHASE_DATE_IN_FUTURE", "The purchase date cannot be in the future.");
        }

        var buyer = buyerName?.Trim();
        if (string.IsNullOrEmpty(buyer) || buyer.Length > MaxBuyerNameLength)
        {
            throw DomainRuleException.Validation(
                "PARISH_PURCHASE_BUYER_REQUIRED",
                $"Name of the person who paid is required and must not exceed {MaxBuyerNameLength} characters.");
        }

        var supplier = string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim();
        if (supplier is { Length: > MaxSupplierLength })
        {
            throw DomainRuleException.Validation("PARISH_PURCHASE_SUPPLIER_TOO_LONG", $"Supplier must not exceed {MaxSupplierLength} characters.");
        }

        if (lines.Count == 0)
        {
            throw DomainRuleException.Validation("PARISH_PURCHASE_ITEMS_REQUIRED", "Record at least one material line.");
        }

        if (lines.Select(line => line.MaterialId).Distinct().Count() != lines.Count)
        {
            throw DomainRuleException.Validation("PARISH_PURCHASE_DUPLICATE_LINE", "Each material can appear only once in a receipt.");
        }

        if (invoiceFileIds.Count == 0)
        {
            throw DomainRuleException.Validation("PARISH_PURCHASE_INVOICE_REQUIRED", "Attach at least one invoice photo.");
        }

        var cleanedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleanedNote is { Length: > MaxNoteLength })
        {
            throw DomainRuleException.Validation("PARISH_PURCHASE_NOTE_TOO_LONG", $"Note must not exceed {MaxNoteLength} characters.");
        }

        var receipt = new ParishPurchaseReceipt
        {
            ParishPurchaseReceiptId = Guid.NewGuid(),
            ParishId = parishId,
            SeasonId = seasonId,
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
            receipt._items.Add(ParishPurchaseItem.Create(receipt.ParishPurchaseReceiptId, line));
        }

        receipt.TotalCost = receipt._items.Sum(item => item.TotalPrice);
        return receipt;
    }

    public Guid ParishPurchaseReceiptId { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid SeasonId { get; private set; }

    /// <summary>Who actually paid for the goods (parish committee member, parishioner…). Not a system account.</summary>
    public string BuyerName { get; private set; } = string.Empty;

    public string? SupplierName { get; private set; }

    public DateOnly PurchaseDate { get; private set; }

    public decimal TotalCost { get; private set; }

    public Guid[] InvoiceFileIds { get; private set; } = [];

    public string? Note { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<ParishPurchaseItem> Items => _items;
}

/// <summary><c>parish_purchase_item</c>: quantity and unit price of one material of a parish purchase.</summary>
public sealed class ParishPurchaseItem
{
    private ParishPurchaseItem()
    {
    }

    public static ParishPurchaseItem Create(Guid receiptId, ParishPurchaseLineInput input)
    {
        if (input.MaterialId == Guid.Empty)
        {
            throw DomainRuleException.Validation("PARISH_PURCHASE_MATERIAL_REQUIRED", "Material is required.");
        }

        var quantity = Resource.Domain.Inventory.Quantity.Positive(input.Quantity, "PARISH_PURCHASE_QUANTITY_INVALID", "Quantity");
        var unitPrice = Price(input.UnitPrice);
        return new ParishPurchaseItem
        {
            ParishPurchaseItemId = Guid.NewGuid(),
            ParishPurchaseReceiptId = receiptId,
            MaterialId = input.MaterialId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TotalPrice = decimal.Round(quantity * unitPrice, 2)
        };
    }

    public Guid ParishPurchaseItemId { get; private set; }

    public Guid ParishPurchaseReceiptId { get; private set; }

    public Guid MaterialId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal TotalPrice { get; private set; }

    private static decimal Price(decimal value)
    {
        if (value < 0 || value > Resource.Domain.Inventory.Quantity.Max || decimal.Round(value, 2) != value)
        {
            throw DomainRuleException.Validation(
                "PARISH_PURCHASE_UNIT_PRICE_INVALID",
                "Unit price must be between 0 and the maximum with at most 2 decimals.");
        }

        return value;
    }
}
