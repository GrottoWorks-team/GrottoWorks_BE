using Resource.Domain.Inventory;

namespace Resource.Domain.Purchases;

/// <summary><c>purchase_request.request_status</c> (ERD v3.6, BA 9.5).</summary>
public enum PurchaseRequestStatus
{
    /// <summary>Preparation state (R-12: no DRAFT in the baseline); empty lines and reason are allowed.</summary>
    Planning,
    PendingApproval,
    Approved,
    Rejected,

    /// <summary>A volunteer accepted the PURCHASE task (<c>task.purchase-task.assigned</c>).</summary>
    BuyerAssigned,

    /// <summary>The buyer recorded the purchase; stock is not increased yet (BR-26).</summary>
    Purchased,
    PartiallyReceived,
    Completed,
    Cancelled
}

/// <summary>One line of a request to put into <see cref="PurchaseRequest.ReplaceItems"/>.</summary>
public sealed record PurchaseRequestItemInput(
    Guid MaterialId,
    decimal RequestedQuantity,
    decimal? EstimatedCost,
    string? Note,
    string? ShortageOverrideReason);

/// <summary>
/// <c>purchase_request</c> (ERD v3.6, F-RES-06/07): created by the material officer of a work area, approved or
/// rejected as a whole by the leader of the same work area (D-01, BR-28/29). Lines are editable only in PLANNING.
/// Estimated total is the sum of the line estimates — never stored separately.
/// </summary>
public sealed class PurchaseRequest
{
    public const int MaxItems = 50;
    public const int MaxReasonLength = 2000;
    public const int MaxDecisionNoteLength = 500;

    private readonly List<PurchaseRequestItem> _items = [];
    private readonly List<PurchaseRequestStateChange> _stateChanges = [];

    private PurchaseRequest()
    {
    }

    public Guid PurchaseRequestId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    /// <summary>Work-area leader who approved <b>or</b> rejected (ERD note, D-01).</summary>
    public Guid? ApprovedByUserId { get; private set; }

    public string? RequestReason { get; private set; }

    public PurchaseRequestStatus Status { get; private set; }

    /// <summary>Required when REJECTED (BR-31); optional approval note otherwise.</summary>
    public string? DecisionNote { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? RejectedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>; every change touches this row.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<PurchaseRequestItem> Items => _items;

    /// <summary>State changes added by this unit of work (history is read through the store).</summary>
    public IReadOnlyList<PurchaseRequestStateChange> StateChanges => _stateChanges;

    /// <summary>Sum of line estimates; lines without an estimate count as 0.</summary>
    public decimal EstimatedTotal => _items.Sum(item => item.EstimatedCost ?? 0);

    public static PurchaseRequest Create(
        Guid workAreaId,
        Guid requestedByUserId,
        string? reason,
        IReadOnlyCollection<PurchaseRequestItemInput> items,
        DateTimeOffset now)
    {
        var request = new PurchaseRequest
        {
            PurchaseRequestId = Guid.NewGuid(),
            WorkAreaId = workAreaId,
            RequestedByUserId = requestedByUserId,
            RequestReason = CleanReason(reason),
            Status = PurchaseRequestStatus.Planning,
            CreatedAt = now.ToUniversalTime()
        };
        request.SetItems(items);
        request.Record(null, PurchaseRequestStatus.Planning, requestedByUserId, null, now);
        return request;
    }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "PURCHASE_REQUEST_VERSION_MISMATCH",
                "The purchase request was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    /// <summary>PATCH semantics: <c>null</c> = unchanged, an empty reason clears it; items replace all lines.</summary>
    public void UpdatePlanning(string? reason, IReadOnlyCollection<PurchaseRequestItemInput>? items, DateTimeOffset now)
    {
        EnsureStatus(PurchaseRequestStatus.Planning, "Only a PLANNING purchase request can be edited.");

        if (reason is not null)
        {
            RequestReason = CleanReason(reason);
        }

        if (items is not null)
        {
            SetItems(items);
        }

        UpdatedAt = now.ToUniversalTime();
    }

    /// <summary>BR-27: at least one line and a reason before it goes to the leader.</summary>
    public void Submit(Guid userId, DateTimeOffset now)
    {
        EnsureStatus(PurchaseRequestStatus.Planning, "Only a PLANNING purchase request can be submitted.");

        if (_items.Count == 0)
        {
            throw DomainRuleException.Validation("PURCHASE_REQUEST_ITEMS_REQUIRED", "Add at least one material before submitting.");
        }

        if (RequestReason is null)
        {
            throw DomainRuleException.Validation("PURCHASE_REQUEST_REASON_REQUIRED", "A reason is required before submitting.");
        }

        Transition(PurchaseRequestStatus.PendingApproval, userId, null, now);
    }

    public void Approve(Guid leaderUserId, string? note, DateTimeOffset now)
    {
        EnsureStatus(PurchaseRequestStatus.PendingApproval, "Only a purchase request waiting for approval can be approved.");

        var cleaned = CleanDecisionNote(note);
        ApprovedByUserId = leaderUserId;
        ApprovedAt = now.ToUniversalTime();
        DecisionNote = cleaned;
        Transition(PurchaseRequestStatus.Approved, leaderUserId, cleaned, now);
    }

    /// <summary>BR-31: a rejection always carries a reason.</summary>
    public void Reject(Guid leaderUserId, string? reason, DateTimeOffset now)
    {
        EnsureStatus(PurchaseRequestStatus.PendingApproval, "Only a purchase request waiting for approval can be rejected.");

        var cleaned = CleanDecisionNote(reason)
            ?? throw DomainRuleException.Validation("PURCHASE_REQUEST_REJECT_REASON_REQUIRED", "A reason is required to reject.");
        ApprovedByUserId = leaderUserId;
        RejectedAt = now.ToUniversalTime();
        DecisionNote = cleaned;
        Transition(PurchaseRequestStatus.Rejected, leaderUserId, cleaned, now);
    }

    /// <summary>Has any line an estimate? Without one there is no approved budget to exceed (BR-32).</summary>
    public bool HasEstimate => _items.Any(item => item.EstimatedCost is not null);

    /// <summary>
    /// <c>task.purchase-task.assigned</c>: a volunteer accepted the PURCHASE task (BR-30). Idempotent; only an approved
    /// request moves.
    /// </summary>
    public bool MarkBuyerAssigned(Guid systemUserId, DateTimeOffset now)
    {
        if (Status != PurchaseRequestStatus.Approved)
        {
            return false;
        }

        Transition(PurchaseRequestStatus.BuyerAssigned, systemUserId, null, now);
        return true;
    }

    /// <summary>F-RES-09: the buyer recorded a purchase — stock unchanged until delivery is confirmed (BR-26).</summary>
    public void MarkPurchased(Guid buyerUserId, DateTimeOffset now)
    {
        if (Status is not (PurchaseRequestStatus.Approved or PurchaseRequestStatus.BuyerAssigned or PurchaseRequestStatus.Purchased
            or PurchaseRequestStatus.PartiallyReceived))
        {
            throw new DomainRuleException("PURCHASE_REQUEST_INVALID_STATE", "Purchases can only be recorded for an approved request that is not completed.");
        }

        if (Status != PurchaseRequestStatus.Purchased)
        {
            Transition(PurchaseRequestStatus.Purchased, buyerUserId, null, now);
        }
    }

    /// <summary>F-RES-10: after a delivery, COMPLETED when every line received its requested usable quantity, else the request stays partly open.</summary>
    public void MarkDelivered(IReadOnlyDictionary<Guid, decimal> deliveredUsableByLine, Guid officerUserId, DateTimeOffset now)
    {
        var completed = _items.All(item => deliveredUsableByLine.GetValueOrDefault(item.PurchaseRequestItemId) >= item.RequestedQuantity);
        var target = completed ? PurchaseRequestStatus.Completed : PurchaseRequestStatus.PartiallyReceived;
        if (Status != target)
        {
            Transition(target, officerUserId, null, now);
        }
    }

    private void SetItems(IReadOnlyCollection<PurchaseRequestItemInput> items)
    {
        if (items.Count > MaxItems)
        {
            throw DomainRuleException.Validation("PURCHASE_REQUEST_TOO_MANY_ITEMS", $"A purchase request holds at most {MaxItems} materials.");
        }

        if (items.GroupBy(item => item.MaterialId).Any(group => group.Count() > 1))
        {
            throw DomainRuleException.Validation(
                "PURCHASE_REQUEST_DUPLICATE_MATERIAL",
                "Each material can appear only once in a purchase request.");
        }

        var lines = items.Select(item => PurchaseRequestItem.Create(PurchaseRequestId, item)).ToList();
        _items.Clear();
        _items.AddRange(lines);
    }

    private void EnsureStatus(PurchaseRequestStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new DomainRuleException("PURCHASE_REQUEST_INVALID_STATE", message);
        }
    }

    private void Transition(PurchaseRequestStatus to, Guid userId, string? note, DateTimeOffset now)
    {
        var from = Status;
        Status = to;
        UpdatedAt = now.ToUniversalTime();
        Record(from, to, userId, note, now);
    }

    private void Record(PurchaseRequestStatus? from, PurchaseRequestStatus to, Guid userId, string? note, DateTimeOffset now) =>
        _stateChanges.Add(PurchaseRequestStateChange.Create(PurchaseRequestId, from, to, userId, note, now));

    private static string? CleanReason(string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is { Length: > MaxReasonLength })
        {
            throw DomainRuleException.Validation("PURCHASE_REQUEST_REASON_TOO_LONG", $"Reason must not exceed {MaxReasonLength} characters.");
        }

        return cleaned;
    }

    private static string? CleanDecisionNote(string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is { Length: > MaxDecisionNoteLength })
        {
            throw DomainRuleException.Validation(
                "PURCHASE_REQUEST_DECISION_NOTE_TOO_LONG",
                $"The decision note must not exceed {MaxDecisionNoteLength} characters.");
        }

        return cleaned;
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary><c>purchase_request_item</c> (ERD v3.6): unique per (request, material), quantity &gt; 0, cost ≥ 0.</summary>
public sealed class PurchaseRequestItem
{
    public const decimal MaxCost = 999_999_999_999.99m;

    private PurchaseRequestItem()
    {
    }

    public Guid PurchaseRequestItemId { get; private set; }

    public Guid PurchaseRequestId { get; private set; }

    public Guid MaterialId { get; private set; }

    public decimal RequestedQuantity { get; private set; }

    /// <summary>Estimated cost of the whole line (not a unit price); <c>null</c> = not estimated.</summary>
    public decimal? EstimatedCost { get; private set; }

    public string? Note { get; private set; }

    /// <summary>
    /// Why the line asks for more than the uncovered shortage (BA 8.6 "override + lý do"). Not in ERD v3.6 —
    /// extension column, see Resource README.
    /// </summary>
    public string? ShortageOverrideReason { get; private set; }

    internal static PurchaseRequestItem Create(Guid requestId, PurchaseRequestItemInput input)
    {
        if (input.EstimatedCost is { } cost && (cost < 0 || cost > MaxCost || decimal.Round(cost, 2) != cost))
        {
            throw DomainRuleException.Validation(
                "PURCHASE_ITEM_COST_INVALID",
                "Estimated cost must be 0 or more with at most 2 decimals.");
        }

        return new PurchaseRequestItem
        {
            PurchaseRequestItemId = Guid.NewGuid(),
            PurchaseRequestId = requestId,
            MaterialId = input.MaterialId,
            RequestedQuantity = Quantity.Positive(input.RequestedQuantity, "PURCHASE_ITEM_QUANTITY_INVALID", "Requested quantity"),
            EstimatedCost = input.EstimatedCost,
            Note = PurchaseRequest.Clean(input.Note),
            ShortageOverrideReason = PurchaseRequest.Clean(input.ShortageOverrideReason)
        };
    }
}

/// <summary>
/// Technical table <c>purchase_request_state_change</c> behind <c>getPurchaseRequestHistory</c> (StateChange[]):
/// one append-only row per status change, including creation (from = null).
/// </summary>
public sealed class PurchaseRequestStateChange
{
    private PurchaseRequestStateChange()
    {
    }

    public Guid PurchaseRequestStateChangeId { get; private set; }

    public Guid PurchaseRequestId { get; private set; }

    public PurchaseRequestStatus? FromStatus { get; private set; }

    public PurchaseRequestStatus ToStatus { get; private set; }

    public Guid ChangedByUserId { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public string? Note { get; private set; }

    internal static PurchaseRequestStateChange Create(
        Guid requestId,
        PurchaseRequestStatus? from,
        PurchaseRequestStatus to,
        Guid userId,
        string? note,
        DateTimeOffset now) =>
        new()
        {
            PurchaseRequestStateChangeId = Guid.NewGuid(),
            PurchaseRequestId = requestId,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = userId,
            ChangedAt = now.ToUniversalTime(),
            Note = note
        };
}
