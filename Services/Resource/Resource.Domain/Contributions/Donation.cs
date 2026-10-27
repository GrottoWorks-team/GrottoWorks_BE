using Resource.Domain.Inventory;

namespace Resource.Domain.Contributions;

/// <summary><c>donation.donation_type</c> (ERD v3.6.2 <c>donation_kind</c>).</summary>
public enum DonationType
{
    Money,
    Material
}

/// <summary><c>donation.donation_status</c> (BA 9.6): direct records start at RECEIVED.</summary>
public enum DonationStatus
{
    Pledged,
    PartiallyReceived,
    Received,
    Cancelled
}

/// <summary>Derived receipt status of a MATERIAL line (Contract: never stored).</summary>
public enum DonationItemStatus
{
    Pledged,
    PartiallyReceived,
    FullyReceived,
    Unusable,
    Cancelled
}

/// <summary>One material promised by a volunteer (F-RES-13b).</summary>
public sealed record PledgedMaterialInput(Guid MaterialId, decimal PledgedQuantity, string? Note);

/// <summary>One line of a later receipt against an existing donation item (F-RES-13c).</summary>
public sealed record ReceiptLineInput(Guid DonationItemId, decimal ReceivedQuantity, decimal UnusableQuantity, string? Note);

/// <summary>The receipt just recorded and the non-blocking warnings it raised (BA 8.7: receiving more than pledged).</summary>
public sealed record RecordedReceipt(DonationReceipt Receipt, bool IsFirstReceipt, IReadOnlyList<string> Warnings);

/// <summary>One material actually received: <paramref name="UnusableQuantity"/> is checked on arrival (BR-38).</summary>
public sealed record ReceivedMaterialInput(Guid MaterialId, decimal ReceivedQuantity, decimal UnusableQuantity, string? Note);

/// <summary>
/// <c>donation</c> (ERD v3.6.2, D-02): money or materials from a <see cref="Donor"/> for a season. A direct record
/// (F-RES-13) is created RECEIVED together with its first <see cref="DonationReceipt"/>; pledges (F-RES-13b) start
/// PLEDGED. Only receipts change money received or stock — never the pledge (BR-39).
/// </summary>
public sealed class Donation
{
    public const decimal MaxAmount = 999_999_999_999.99m;
    public const int MaxItems = 50;
    public const int MaxNoteLength = 2000;
    public const int MaxCancelReasonLength = 500;

    /// <summary>Clock skew tolerated for "received now" timestamps sent by clients.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    private readonly List<DonationItem> _items = [];
    private readonly List<DonationReceipt> _receipts = [];

    private Donation()
    {
    }

    public Guid DonationId { get; private set; }

    public Guid DonorId { get; private set; }

    public Guid SeasonId { get; private set; }

    /// <summary>Receiving work area: required for MATERIAL (stock is per work area, BR-24), optional for MONEY.</summary>
    public Guid? WorkAreaId { get; private set; }

    public DonationType Type { get; private set; }

    public DonationStatus Status { get; private set; }

    /// <summary>MONEY pledge in VND; null for direct records and MATERIAL.</summary>
    public decimal? PledgedAmount { get; private set; }

    /// <summary>MONEY actually received (sum of receipts); null for MATERIAL.</summary>
    public decimal? Amount { get; private set; }

    /// <summary>Pledge date, or receipt date for direct records.</summary>
    public DateTimeOffset DonationDate { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<DonationItem> Items => _items;

    public IReadOnlyList<DonationReceipt> Receipts => _receipts;

    /// <summary>
    /// F-RES-13 / BA 8.7 "ghi nhận trực tiếp": a donation already in hand. MONEY needs a positive amount and no items
    /// (BR-36); MATERIAL needs a receiving work area and at least one item (BR-37), each received &gt; 0 with
    /// 0 ≤ unusable ≤ received. The donation and its first receipt are built together.
    /// </summary>
    public static Donation RecordDirect(
        Guid donorId,
        Guid seasonId,
        Guid? workAreaId,
        DonationType type,
        decimal? amount,
        IReadOnlyCollection<ReceivedMaterialInput> items,
        DateTimeOffset receivedAt,
        string? note,
        Guid recordedByUserId,
        DateTimeOffset now)
    {
        if (receivedAt > now + FutureTolerance)
        {
            throw DomainRuleException.Validation("DONATION_RECEIVED_IN_FUTURE", "The receipt time cannot be in the future.");
        }

        var donation = new Donation
        {
            DonationId = Guid.NewGuid(),
            DonorId = donorId,
            SeasonId = seasonId,
            WorkAreaId = workAreaId,
            Type = type,
            Status = DonationStatus.Received,
            DonationDate = receivedAt.ToUniversalTime(),
            CreatedByUserId = recordedByUserId,
            CreatedAt = now.ToUniversalTime(),
            Note = CleanNote(note)
        };

        if (type == DonationType.Money)
        {
            if (items.Count > 0)
            {
                throw DomainRuleException.Validation("DONATION_MONEY_HAS_ITEMS", "A MONEY donation has no material items (BR-36).");
            }

            var received = Money(amount ?? throw DomainRuleException.Validation(
                "DONATION_AMOUNT_REQUIRED", "A MONEY donation needs the amount received."));
            donation.Amount = received;
            donation._receipts.Add(DonationReceipt.ForMoney(donation.DonationId, received, receivedAt, recordedByUserId, null, now));
            return donation;
        }

        if (amount is not null)
        {
            throw DomainRuleException.Validation("DONATION_MATERIAL_HAS_AMOUNT", "A MATERIAL donation has no money amount.");
        }

        if (workAreaId is null)
        {
            throw DomainRuleException.Validation("DONATION_WORK_AREA_REQUIRED", "A MATERIAL donation needs the receiving work area (BR-37).");
        }

        if (items.Count == 0)
        {
            throw DomainRuleException.Validation("DONATION_ITEMS_REQUIRED", "A MATERIAL donation needs at least one item (BR-37).");
        }

        if (items.Count > MaxItems)
        {
            throw DomainRuleException.Validation("DONATION_TOO_MANY_ITEMS", $"A donation holds at most {MaxItems} materials.");
        }

        if (items.GroupBy(item => item.MaterialId).Any(group => group.Count() > 1))
        {
            throw DomainRuleException.Validation("DONATION_DUPLICATE_MATERIAL", "Each material can appear only once in a donation.");
        }

        var receipt = DonationReceipt.ForMaterial(donation.DonationId, receivedAt, recordedByUserId, null, now);
        foreach (var input in items)
        {
            var item = DonationItem.Direct(donation.DonationId, input.MaterialId, input.Note);
            receipt.AddLine(item, input.ReceivedQuantity, input.UnusableQuantity, null);
            donation._items.Add(item);
        }

        donation._receipts.Add(receipt);
        return donation;
    }

    /// <summary>
    /// F-RES-13b <c>pledgeMaterialDonation</c>: a volunteer promises materials for a work area (BR-37). PLEDGED; nothing
    /// is posted to stock until a receipt (BR-39).
    /// </summary>
    public static Donation PledgeMaterial(
        Guid donorId,
        Guid seasonId,
        Guid workAreaId,
        IReadOnlyCollection<PledgedMaterialInput> items,
        string? note,
        Guid volunteerUserId,
        DateTimeOffset now)
    {
        if (items.Count == 0)
        {
            throw DomainRuleException.Validation("DONATION_ITEMS_REQUIRED", "A MATERIAL pledge needs at least one item (BR-37).");
        }

        if (items.Count > MaxItems)
        {
            throw DomainRuleException.Validation("DONATION_TOO_MANY_ITEMS", $"A donation holds at most {MaxItems} materials.");
        }

        if (items.GroupBy(item => item.MaterialId).Any(group => group.Count() > 1))
        {
            throw DomainRuleException.Validation("DONATION_DUPLICATE_MATERIAL", "Each material can appear only once in a donation.");
        }

        var donation = Pledged(donorId, seasonId, workAreaId, DonationType.Material, note, volunteerUserId, now);
        foreach (var input in items)
        {
            donation._items.Add(DonationItem.Pledged(donation.DonationId, input.MaterialId, input.PledgedQuantity, input.Note));
        }

        return donation;
    }

    /// <summary>F-RES-13b <c>pledgeMonetarySupport</c>: promised money in VND — never counted as received (BR-39).</summary>
    public static Donation PledgeMoney(
        Guid donorId,
        Guid seasonId,
        Guid? workAreaId,
        decimal amount,
        string? note,
        Guid volunteerUserId,
        DateTimeOffset now)
    {
        var donation = Pledged(donorId, seasonId, workAreaId, DonationType.Money, note, volunteerUserId, now);
        donation.PledgedAmount = Money(amount);
        return donation;
    }

    /// <summary>
    /// F-RES-13b <c>cancelDonationPledge</c>: cancels the outstanding part of a pledge (reason required); receipts
    /// already recorded stay as they are (BA 8.7 step 10).
    /// </summary>
    public void CancelPledge(string? reason, DateTimeOffset now)
    {
        if (Status is not (DonationStatus.Pledged or DonationStatus.PartiallyReceived))
        {
            throw new DomainRuleException("DONATION_INVALID_STATE", "Only an outstanding pledge can be cancelled.");
        }

        var cleaned = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleaned is null || cleaned.Length > MaxCancelReasonLength)
        {
            throw DomainRuleException.Validation(
                "DONATION_CANCEL_REASON_REQUIRED",
                $"A reason of at most {MaxCancelReasonLength} characters is required to cancel a pledge.");
        }

        Status = DonationStatus.Cancelled;
        CancelReason = cleaned;
        UpdatedAt = now.ToUniversalTime();
    }

    /// <summary>
    /// F-RES-13c <c>recordDonationReceipt</c> (BR-38): one physical receipt of a pledge. Totals are cumulated, the status
    /// moves PLEDGED → PARTIALLY_RECEIVED → RECEIVED once everything promised arrived. Receiving more than pledged is
    /// allowed and only warned about. A cancelled or fully received donation takes no more receipts (later deliveries
    /// are new donations).
    /// </summary>
    public RecordedReceipt RecordReceipt(
        DateTimeOffset receivedAt,
        decimal? amount,
        IReadOnlyCollection<ReceiptLineInput> lines,
        string? note,
        Guid receivedByUserId,
        DateTimeOffset now)
    {
        if (Status is not (DonationStatus.Pledged or DonationStatus.PartiallyReceived))
        {
            throw new DomainRuleException("DONATION_INVALID_STATE", "Receipts can only be recorded for an outstanding pledge.");
        }

        if (receivedAt > now + FutureTolerance)
        {
            throw DomainRuleException.Validation("DONATION_RECEIVED_IN_FUTURE", "The receipt time cannot be in the future.");
        }

        var isFirst = _receipts.Count == 0;
        var warnings = new List<string>();
        var cleanedNote = CleanNote(note);
        DonationReceipt receipt;

        if (Type == DonationType.Money)
        {
            if (lines.Count > 0)
            {
                throw DomainRuleException.Validation("DONATION_MONEY_HAS_ITEMS", "A MONEY donation has no material items (BR-36).");
            }

            var received = Money(amount ?? throw DomainRuleException.Validation(
                "DONATION_AMOUNT_REQUIRED", "A MONEY receipt needs the amount received."));
            receipt = DonationReceipt.ForMoney(DonationId, received, receivedAt, receivedByUserId, cleanedNote, now);
            Amount = (Amount ?? 0) + received;
            if (PledgedAmount is { } pledged && Amount > pledged)
            {
                warnings.Add($"Received {Amount} VND in total, more than the {pledged} VND pledged.");
            }

            Status = PledgedAmount is { } promised && Amount < promised ? DonationStatus.PartiallyReceived : DonationStatus.Received;
        }
        else
        {
            if (amount is not null)
            {
                throw DomainRuleException.Validation("DONATION_MATERIAL_HAS_AMOUNT", "A MATERIAL donation has no money amount.");
            }

            if (lines.Count == 0)
            {
                throw DomainRuleException.Validation("DONATION_RECEIPT_ITEMS_REQUIRED", "A receipt needs at least one received item.");
            }

            if (lines.GroupBy(line => line.DonationItemId).Any(group => group.Count() > 1))
            {
                throw DomainRuleException.Validation("DONATION_RECEIPT_DUPLICATE_ITEM", "Each donation item can appear only once in a receipt.");
            }

            receipt = DonationReceipt.ForMaterial(DonationId, receivedAt, receivedByUserId, cleanedNote, now);
            foreach (var line in lines)
            {
                var item = _items.FirstOrDefault(candidate => candidate.DonationItemId == line.DonationItemId)
                    ?? throw DomainRuleException.Validation("DONATION_ITEM_UNKNOWN", $"Item {line.DonationItemId} does not belong to this donation.");
                receipt.AddLine(item, line.ReceivedQuantity, line.UnusableQuantity, line.Note);
                if (item.PledgedQuantity is { } pledged && item.ReceivedQuantity > pledged)
                {
                    warnings.Add($"Item {item.DonationItemId}: received {item.ReceivedQuantity} in total, more than the {pledged} pledged.");
                }
            }

            Status = _items.All(item => item.OutstandingQuantity == 0) ? DonationStatus.Received : DonationStatus.PartiallyReceived;
        }

        _receipts.Add(receipt);
        UpdatedAt = now.ToUniversalTime();
        return new RecordedReceipt(receipt, isFirst, warnings);
    }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "DONATION_VERSION_MISMATCH",
                "The donation was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    private static Donation Pledged(Guid donorId, Guid seasonId, Guid? workAreaId, DonationType type, string? note, Guid userId, DateTimeOffset now) =>
        new()
        {
            DonationId = Guid.NewGuid(),
            DonorId = donorId,
            SeasonId = seasonId,
            WorkAreaId = workAreaId,
            Type = type,
            Status = DonationStatus.Pledged,
            DonationDate = now.ToUniversalTime(),
            CreatedByUserId = userId,
            CreatedAt = now.ToUniversalTime(),
            Note = CleanNote(note)
        };

    internal static decimal Money(decimal value)
    {
        if (value <= 0 || value > MaxAmount || decimal.Round(value, 2) != value)
        {
            throw DomainRuleException.Validation("DONATION_AMOUNT_INVALID", "Amount must be greater than 0 with at most 2 decimals.");
        }

        return value;
    }

    private static string? CleanNote(string? value)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (cleaned is { Length: > MaxNoteLength })
        {
            throw DomainRuleException.Validation("DONATION_NOTE_TOO_LONG", $"Note must not exceed {MaxNoteLength} characters.");
        }

        return cleaned;
    }
}

/// <summary>
/// <c>donation_item</c> (ERD v3.6.2): one material of a MATERIAL donation. Received and unusable totals are caches of
/// the receipt lines; stock is posted per receipt line, never from this row.
/// </summary>
public sealed class DonationItem
{
    private DonationItem()
    {
    }

    public Guid DonationItemId { get; private set; }

    public Guid DonationId { get; private set; }

    public Guid MaterialId { get; private set; }

    /// <summary>Promised quantity of a pledge; null for direct receipts.</summary>
    public decimal? PledgedQuantity { get; private set; }

    public decimal ReceivedQuantity { get; private set; }

    public decimal UnusableQuantity { get; private set; }

    public decimal UsableQuantity => ReceivedQuantity - UnusableQuantity;

    public string? Note { get; private set; }

    public DonationItemStatus StatusIn(DonationStatus donationStatus)
    {
        if (ReceivedQuantity > 0 && UsableQuantity == 0)
        {
            return DonationItemStatus.Unusable;
        }

        if (PledgedQuantity is not { } pledged || ReceivedQuantity >= pledged)
        {
            return ReceivedQuantity > 0 ? DonationItemStatus.FullyReceived : DonationItemStatus.Pledged;
        }

        if (donationStatus == DonationStatus.Cancelled)
        {
            return DonationItemStatus.Cancelled;
        }

        return ReceivedQuantity > 0 ? DonationItemStatus.PartiallyReceived : DonationItemStatus.Pledged;
    }

    internal static DonationItem Pledged(Guid donationId, Guid materialId, decimal pledgedQuantity, string? note) =>
        new()
        {
            DonationItemId = Guid.NewGuid(),
            DonationId = donationId,
            MaterialId = materialId,
            PledgedQuantity = Quantity.Positive(pledgedQuantity, "DONATION_PLEDGED_QUANTITY_INVALID", "Pledged quantity"),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };

    /// <summary>Promised but not yet received; 0 for direct receipts or once fully received.</summary>
    public decimal OutstandingQuantity => PledgedQuantity is { } pledged ? Math.Max(0, pledged - ReceivedQuantity) : 0;

    internal static DonationItem Direct(Guid donationId, Guid materialId, string? note) =>
        new()
        {
            DonationItemId = Guid.NewGuid(),
            DonationId = donationId,
            MaterialId = materialId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };

    internal void AddReceived(decimal received, decimal unusable)
    {
        ReceivedQuantity += received;
        UnusableQuantity += unusable;
    }
}

/// <summary>
/// <c>donation_receipt</c> (ERD v3.6.2): one physical receipt occasion. Header, lines, stock posting of the usable
/// quantity and the donation totals are saved in one transaction (BR-38).
/// </summary>
public sealed class DonationReceipt
{
    private readonly List<DonationReceiptItem> _items = [];

    private DonationReceipt()
    {
    }

    public Guid DonationReceiptId { get; private set; }

    public Guid DonationId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public Guid ReceivedByUserId { get; private set; }

    /// <summary>MONEY only.</summary>
    public decimal? ReceivedAmount { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<DonationReceiptItem> Items => _items;

    internal static DonationReceipt ForMoney(Guid donationId, decimal amount, DateTimeOffset receivedAt, Guid userId, string? note, DateTimeOffset now) =>
        new()
        {
            DonationReceiptId = Guid.NewGuid(),
            DonationId = donationId,
            ReceivedAt = receivedAt.ToUniversalTime(),
            ReceivedByUserId = userId,
            ReceivedAmount = amount,
            Note = note,
            CreatedAt = now.ToUniversalTime()
        };

    internal static DonationReceipt ForMaterial(Guid donationId, DateTimeOffset receivedAt, Guid userId, string? note, DateTimeOffset now) =>
        new()
        {
            DonationReceiptId = Guid.NewGuid(),
            DonationId = donationId,
            ReceivedAt = receivedAt.ToUniversalTime(),
            ReceivedByUserId = userId,
            Note = note,
            CreatedAt = now.ToUniversalTime()
        };

    internal void AddLine(DonationItem item, decimal received, decimal unusable, string? note)
    {
        var line = DonationReceiptItem.Create(DonationReceiptId, item, received, unusable, note);
        item.AddReceived(line.ReceivedQuantity, line.UnusableQuantity);
        _items.Add(line);
    }
}

/// <summary><c>donation_receipt_item</c> (ERD v3.6.2): usable = received − unusable is what goes to stock.</summary>
public sealed class DonationReceiptItem
{
    private DonationReceiptItem()
    {
    }

    public Guid DonationReceiptItemId { get; private set; }

    public Guid DonationReceiptId { get; private set; }

    public Guid DonationItemId { get; private set; }

    public decimal ReceivedQuantity { get; private set; }

    public decimal UnusableQuantity { get; private set; }

    public decimal UsableQuantity => ReceivedQuantity - UnusableQuantity;

    public string? Note { get; private set; }

    internal static DonationReceiptItem Create(Guid receiptId, DonationItem item, decimal received, decimal unusable, string? note)
    {
        var receivedQuantity = Quantity.Positive(received, "DONATION_RECEIVED_QUANTITY_INVALID", "Received quantity");
        if (unusable < 0 || unusable > receivedQuantity || decimal.Round(unusable, 2) != unusable)
        {
            throw DomainRuleException.Validation(
                "DONATION_UNUSABLE_QUANTITY_INVALID",
                "Unusable quantity must be between 0 and the received quantity, with at most 2 decimals.");
        }

        return new DonationReceiptItem
        {
            DonationReceiptItemId = Guid.NewGuid(),
            DonationReceiptId = receiptId,
            DonationItemId = item.DonationItemId,
            ReceivedQuantity = receivedQuantity,
            UnusableQuantity = unusable,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
    }
}
