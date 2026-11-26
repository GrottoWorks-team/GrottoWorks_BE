namespace Resource.Domain.Inventory;

/// <summary><c>parish_distribution.distribution_status</c>: stock left the parish warehouse and is on its way.</summary>
public enum ParishDistributionStatus
{
    /// <summary>DISTRIBUTION_OUT posted; waiting for the material officer of the target work area.</summary>
    InTransit,

    /// <summary>The officer confirmed what arrived; the usable part is posted as TRANSFER_IN.</summary>
    Received,

    /// <summary>Parish withdrew the shipment before receipt; DISTRIBUTION_RETURN restores the stock.</summary>
    Cancelled
}

/// <summary>Why a distribution exists: a manual issue or the automatic hand-down of a confirmed purchase.</summary>
public enum ParishDistributionOrigin
{
    Manual,
    Purchase
}

public sealed record DistributionLineInput(Guid MaterialId, decimal Quantity);

public sealed record DistributionReceiptLineInput(Guid ItemId, decimal UsableQuantity, decimal UnusableQuantity);

/// <summary>
/// <c>parish_distribution</c> (two-step hand-down, F-RES-21): parish issues stock from the warehouse, the material
/// officer of the target work area confirms the usable/unusable quantity. Stock is out of the parish ledger at issue
/// and reaches the work-area ledger only at confirmation, so an in-transit shipment is never counted twice.
/// </summary>
public sealed class ParishDistribution
{
    public const int MaxItems = 50;
    public const int MaxNoteLength = 2000;
    public const int MaxReasonLength = 500;

    private readonly List<ParishDistributionItem> _items = [];

    private ParishDistribution()
    {
    }

    public Guid ParishDistributionId { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid TargetWorkAreaId { get; private set; }

    public ParishDistributionStatus Status { get; private set; }

    public ParishDistributionOrigin Origin { get; private set; }

    public Guid IssuedByUserId { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public Guid? ReceivedConfirmedByUserId { get; private set; }

    public DateTimeOffset? ReceivedConfirmedAt { get; private set; }

    public string? Note { get; private set; }

    public string? CancelReason { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<ParishDistributionItem> Items => _items;

    public static ParishDistribution Issue(
        Guid parishId,
        Guid seasonId,
        Guid targetWorkAreaId,
        IReadOnlyCollection<DistributionLineInput> lines,
        ParishDistributionOrigin origin,
        string? note,
        Guid issuedByUserId,
        DateTimeOffset now)
    {
        if (parishId == Guid.Empty || seasonId == Guid.Empty || targetWorkAreaId == Guid.Empty)
        {
            throw DomainRuleException.Validation("PARISH_DISTRIBUTION_INVALID", "Parish, season and target work area are required.");
        }

        if (lines.Count == 0)
        {
            throw DomainRuleException.Validation("PARISH_DISTRIBUTION_ITEMS_REQUIRED", "Issue at least one material.");
        }

        if (lines.Count > MaxItems)
        {
            throw DomainRuleException.Validation("PARISH_DISTRIBUTION_TOO_MANY_ITEMS", $"A distribution holds at most {MaxItems} materials.");
        }

        if (lines.GroupBy(line => line.MaterialId).Any(group => group.Count() > 1))
        {
            throw DomainRuleException.Validation("PARISH_DISTRIBUTION_DUPLICATE_MATERIAL", "Each material can appear only once in a distribution.");
        }

        var cleaned = Clean(note);
        if (cleaned is { Length: > MaxNoteLength })
        {
            throw DomainRuleException.Validation("PARISH_DISTRIBUTION_NOTE_TOO_LONG", $"Note must not exceed {MaxNoteLength} characters.");
        }

        var distribution = new ParishDistribution
        {
            ParishDistributionId = Guid.NewGuid(),
            ParishId = parishId,
            SeasonId = seasonId,
            TargetWorkAreaId = targetWorkAreaId,
            Status = ParishDistributionStatus.InTransit,
            Origin = origin,
            IssuedByUserId = issuedByUserId,
            IssuedAt = now.ToUniversalTime(),
            Note = cleaned
        };
        foreach (var line in lines)
        {
            distribution._items.Add(ParishDistributionItem.Issue(distribution.ParishDistributionId, line));
        }

        return distribution;
    }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "PARISH_DISTRIBUTION_VERSION_MISMATCH",
                "The distribution was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    /// <summary>One officer of the target work area confirms every line exactly once (no second confirmation).</summary>
    public void ConfirmReceipt(
        IReadOnlyCollection<DistributionReceiptLineInput> lines,
        string? note,
        Guid officerUserId,
        DateTimeOffset now)
    {
        EnsureInTransit("Only an in-transit distribution can be received.");

        if (lines.Count != _items.Count || lines.Select(line => line.ItemId).Distinct().Count() != _items.Count)
        {
            throw DomainRuleException.Validation("PARISH_DISTRIBUTION_RECEIPT_ITEMS_MISMATCH", "Confirm every issued line exactly once.");
        }

        foreach (var line in lines)
        {
            var item = _items.FirstOrDefault(candidate => candidate.ParishDistributionItemId == line.ItemId)
                ?? throw DomainRuleException.Validation(
                    "PARISH_DISTRIBUTION_RECEIPT_ITEMS_MISMATCH",
                    $"Line {line.ItemId} is not part of this distribution.");
            item.SetReceived(line.UsableQuantity, line.UnusableQuantity);
        }

        var cleaned = Clean(note);
        if (cleaned is { Length: > MaxNoteLength })
        {
            throw DomainRuleException.Validation("PARISH_DISTRIBUTION_NOTE_TOO_LONG", $"Note must not exceed {MaxNoteLength} characters.");
        }

        Status = ParishDistributionStatus.Received;
        ReceivedConfirmedByUserId = officerUserId;
        ReceivedConfirmedAt = now.ToUniversalTime();
        if (cleaned is not null)
        {
            Note = cleaned;
        }
    }

    /// <summary>Parish withdraws a shipment that was not received yet; the caller restores the stock.</summary>
    public void Cancel(string? reason, DateTimeOffset now)
    {
        EnsureInTransit("Only an in-transit distribution can be cancelled.");

        var cleaned = Clean(reason)
            ?? throw DomainRuleException.Validation("PARISH_DISTRIBUTION_CANCEL_REASON_REQUIRED", "A reason is required to cancel.");
        if (cleaned.Length > MaxReasonLength)
        {
            throw DomainRuleException.Validation(
                "PARISH_DISTRIBUTION_CANCEL_REASON_TOO_LONG",
                $"The reason must not exceed {MaxReasonLength} characters.");
        }

        Status = ParishDistributionStatus.Cancelled;
        CancelReason = cleaned;
        ReceivedConfirmedAt = now.ToUniversalTime();
    }

    private void EnsureInTransit(string message)
    {
        if (Status != ParishDistributionStatus.InTransit)
        {
            throw new DomainRuleException("PARISH_DISTRIBUTION_INVALID_STATE", message);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary><c>parish_distribution_item</c>: issued quantity plus what the work-area officer counted on arrival.</summary>
public sealed class ParishDistributionItem
{
    private ParishDistributionItem()
    {
    }

    public Guid ParishDistributionItemId { get; private set; }

    public Guid ParishDistributionId { get; private set; }

    public Guid MaterialId { get; private set; }

    public decimal IssuedQuantity { get; private set; }

    public decimal? ReceivedUsableQuantity { get; private set; }

    public decimal? ReceivedUnusableQuantity { get; private set; }

    internal static ParishDistributionItem Issue(Guid distributionId, DistributionLineInput line) =>
        new()
        {
            ParishDistributionItemId = Guid.NewGuid(),
            ParishDistributionId = distributionId,
            MaterialId = line.MaterialId,
            IssuedQuantity = Quantity.Positive(line.Quantity, "PARISH_DISTRIBUTION_QUANTITY_INVALID", "Issued quantity")
        };

    internal void SetReceived(decimal usable, decimal unusable)
    {
        if (usable < 0 || unusable < 0 || decimal.Round(usable, 2) != usable || decimal.Round(unusable, 2) != unusable ||
            usable + unusable > IssuedQuantity)
        {
            throw DomainRuleException.Validation(
                "PARISH_DISTRIBUTION_RECEIPT_INVALID",
                $"Usable and unusable quantities must be 0 or more and together at most the {IssuedQuantity} issued.");
        }

        ReceivedUsableQuantity = usable;
        ReceivedUnusableQuantity = unusable;
    }
}
