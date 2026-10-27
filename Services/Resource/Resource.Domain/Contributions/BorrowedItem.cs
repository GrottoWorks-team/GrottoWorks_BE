namespace Resource.Domain.Contributions;

/// <summary><c>borrowed_item.borrowed_item_status</c> (ERD v3.6.2, BA 9.7); OVERDUE is derived, never stored.</summary>
public enum BorrowedItemStatus
{
    Offered,
    Accepted,
    Rejected,
    Issued,
    InUse,
    Returned
}

/// <summary><c>borrow_transaction.return_condition</c> (BR-42).</summary>
public enum ReturnCondition
{
    Good,
    Damaged,
    Lost
}

/// <summary>What a volunteer enters for a loan offer (D-05), possibly on behalf of a lender without an account.</summary>
public sealed record LoanOfferInput(
    Guid SeasonId,
    Guid? WorkAreaId,
    string? OwnerName,
    string? OwnerPhone,
    string? OwnerAddress,
    string? OwnerNote,
    string? ItemName,
    decimal Quantity,
    string? Description,
    string? InitialCondition,
    DateOnly ExpectedReturnDate);

/// <summary>
/// <c>borrowed_item</c> (ERD v3.6.2, F-RES-18): equipment lent to the parish. Not a catalog material and never in the
/// stock ledger. OFFERED (entered by a volunteer, who stays the contact point) → ACCEPTED / REJECTED by the officer →
/// ISSUED (received) → IN_USE (assigned to a work area or task) → RETURNED with a condition (BR-42). Issue, incidents
/// and the return are rows of <see cref="BorrowTransaction"/>.
/// </summary>
public sealed class BorrowedItem
{
    public const int MaxNameLength = 150;
    public const int MaxPhoneLength = 30;
    public const int MaxAddressLength = 255;
    public const int MaxConditionLength = 50;
    public const int MaxTextLength = 2000;
    public const int MaxReasonLength = 500;

    private readonly List<BorrowTransaction> _transactions = [];

    private BorrowedItem()
    {
    }

    public Guid BorrowedItemId { get; private set; }

    public Guid OfferedByUserId { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public Guid? WorkAreaId { get; private set; }

    public Guid? TaskId { get; private set; }

    public Guid SeasonId { get; private set; }

    public string OwnerName { get; private set; } = string.Empty;

    public string? OwnerPhone { get; private set; }

    public string? OwnerAddress { get; private set; }

    public string? OwnerNote { get; private set; }

    public string ItemName { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public string? Description { get; private set; }

    public string? InitialCondition { get; private set; }

    public DateOnly ExpectedReturnDate { get; private set; }

    public BorrowedItemStatus Status { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Why the officer rejected the offer (not in ERD v3.6.2 — extension column).</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Last day a return-due / overdue reminder went out (technical column, avoids duplicate reminders).</summary>
    public DateOnly? LastRemindedOn { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<BorrowTransaction> Transactions => _transactions;

    public bool IsOut => Status is BorrowedItemStatus.Issued or BorrowedItemStatus.InUse;

    public bool IsOverdue(DateOnly today) => IsOut && ExpectedReturnDate < today;

    /// <summary>BR-41: lender name and expected return date are required; the lender needs no account.</summary>
    public static BorrowedItem Offer(LoanOfferInput input, Guid offeredByUserId, DateOnly today, DateTimeOffset now)
    {
        if (input.ExpectedReturnDate < today)
        {
            throw DomainRuleException.Validation("LOAN_RETURN_DATE_IN_PAST", "The expected return date cannot be in the past.");
        }

        return new BorrowedItem
        {
            BorrowedItemId = Guid.NewGuid(),
            OfferedByUserId = offeredByUserId,
            WorkAreaId = input.WorkAreaId,
            SeasonId = input.SeasonId,
            OwnerName = Required(input.OwnerName, MaxNameLength, "LOAN_OWNER_NAME_REQUIRED", "Lender name"),
            OwnerPhone = Optional(input.OwnerPhone, MaxPhoneLength, "Lender phone"),
            OwnerAddress = Optional(input.OwnerAddress, MaxAddressLength, "Lender address"),
            OwnerNote = Optional(input.OwnerNote, MaxTextLength, "Lender note"),
            ItemName = Required(input.ItemName, MaxNameLength, "LOAN_ITEM_NAME_REQUIRED", "Item name"),
            Quantity = Inventory.Quantity.Positive(input.Quantity, "LOAN_QUANTITY_INVALID", "Quantity"),
            Description = Optional(input.Description, MaxTextLength, "Description"),
            InitialCondition = Optional(input.InitialCondition, MaxConditionLength, "Initial condition"),
            ExpectedReturnDate = input.ExpectedReturnDate,
            Status = BorrowedItemStatus.Offered,
            CreatedAt = now.ToUniversalTime()
        };
    }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "BORROWED_ITEM_VERSION_MISMATCH",
                "The item was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    public void Accept(Guid officerUserId, Guid? workAreaId, DateOnly? expectedReturnDate, DateOnly today, DateTimeOffset now)
    {
        EnsureStatus("Only an offered item can be accepted.", BorrowedItemStatus.Offered);
        if (expectedReturnDate is { } date)
        {
            if (date < today)
            {
                throw DomainRuleException.Validation("LOAN_RETURN_DATE_IN_PAST", "The expected return date cannot be in the past.");
            }

            ExpectedReturnDate = date;
        }

        WorkAreaId = workAreaId ?? WorkAreaId;
        Status = BorrowedItemStatus.Accepted;
        AcceptedByUserId = officerUserId;
        AcceptedAt = now.ToUniversalTime();
    }

    public void Reject(string? reason)
    {
        EnsureStatus("Only an offered item can be rejected.", BorrowedItemStatus.Offered);
        RejectionReason = Required(reason, MaxReasonLength, "LOAN_REJECT_REASON_REQUIRED", "Reason");
        Status = BorrowedItemStatus.Rejected;
    }

    /// <summary>The officer received the item: ACCEPTED → ISSUED with a borrow date.</summary>
    public void Issue(DateOnly borrowDate, string? note, Guid officerUserId, DateOnly today)
    {
        EnsureStatus("Only an accepted item can be issued.", BorrowedItemStatus.Accepted);
        if (borrowDate > today)
        {
            throw DomainRuleException.Validation("LOAN_BORROW_DATE_IN_FUTURE", "The borrow date cannot be in the future.");
        }

        _transactions.Add(BorrowTransaction.Issued(BorrowedItemId, borrowDate, officerUserId, Optional(note, MaxTextLength, "Note")));
        Status = BorrowedItemStatus.Issued;
    }

    /// <summary>ISSUED / IN_USE → IN_USE for a work area (and optionally one of its tasks) of the same season.</summary>
    public void Assign(Guid workAreaId, Guid? taskId)
    {
        EnsureStatus("Only an issued item can be assigned.", BorrowedItemStatus.Issued, BorrowedItemStatus.InUse);
        WorkAreaId = workAreaId;
        TaskId = taskId;
        Status = BorrowedItemStatus.InUse;
    }

    /// <summary>Damage or loss while the item is out; the item stays out until it is returned (or returned as LOST).</summary>
    public BorrowTransaction RecordIncident(ReturnCondition condition, decimal? quantity, DateOnly date, string? note, Guid officerUserId, DateOnly today)
    {
        if (!IsOut)
        {
            throw new DomainRuleException("BORROWED_ITEM_INVALID_STATE", "Incidents are recorded only while the item is out.");
        }

        if (condition == ReturnCondition.Good)
        {
            throw DomainRuleException.Validation("LOAN_INCIDENT_CONDITION_INVALID", "An incident is either DAMAGED or LOST.");
        }

        if (date > today)
        {
            throw DomainRuleException.Validation("LOAN_INCIDENT_DATE_IN_FUTURE", "The incident date cannot be in the future.");
        }

        var affected = CheckQuantity(quantity ?? Quantity);
        var incident = BorrowTransaction.Incident(BorrowedItemId, IssueDate, date, condition, affected, officerUserId,
            Required(note, MaxTextLength, "LOAN_INCIDENT_NOTE_REQUIRED", "Incident description"));
        _transactions.Add(incident);
        return incident;
    }

    /// <summary>BR-42: the return always records its condition; RETURNED closes the loan.</summary>
    public BorrowTransaction Return(DateOnly returnDate, ReturnCondition condition, decimal? returnedQuantity, string? note, Guid officerUserId, DateOnly today)
    {
        if (!IsOut)
        {
            throw new DomainRuleException("BORROWED_ITEM_INVALID_STATE", "Only an issued or in-use item can be returned.");
        }

        if (returnDate > today || returnDate < IssueDate)
        {
            throw DomainRuleException.Validation("LOAN_RETURN_DATE_INVALID", "The return date must be between the borrow date and today.");
        }

        var issue = _transactions.Single(transaction => transaction.IsIssue);
        issue.Close(returnDate, condition, CheckQuantity(returnedQuantity ?? (condition == ReturnCondition.Lost ? 0 : Quantity), allowZero: true),
            officerUserId, Optional(note, MaxTextLength, "Note"));
        Status = BorrowedItemStatus.Returned;
        return issue;
    }

    public void MarkReminded(DateOnly today) => LastRemindedOn = today;

    private DateOnly IssueDate => _transactions.Single(transaction => transaction.IsIssue).BorrowDate;

    private decimal CheckQuantity(decimal quantity, bool allowZero = false)
    {
        if ((allowZero ? quantity < 0 : quantity <= 0) || quantity > Quantity || decimal.Round(quantity, 2) != quantity)
        {
            throw DomainRuleException.Validation("LOAN_QUANTITY_INVALID", $"Quantity must be between 0 and the {Quantity} borrowed.");
        }

        return quantity;
    }

    private void EnsureStatus(string message, params BorrowedItemStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainRuleException("BORROWED_ITEM_INVALID_STATE", message);
        }
    }

    private static string Required(string? value, int maxLength, string code, string field)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrEmpty(cleaned) || cleaned.Length > maxLength)
        {
            throw DomainRuleException.Validation(code, $"{field} is required and at most {maxLength} characters.");
        }

        return cleaned;
    }

    private static string? Optional(string? value, int maxLength, string field)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (cleaned is { Length: var length } && length > maxLength)
        {
            throw DomainRuleException.Validation("LOAN_FIELD_TOO_LONG", $"{field} must not exceed {maxLength} characters.");
        }

        return cleaned;
    }
}

/// <summary>
/// <c>borrow_transaction</c> (ERD v3.6.2): the issue row (closed by the return with its condition, BR-42) and incident
/// rows (DAMAGED / LOST while out, no return date).
/// </summary>
public sealed class BorrowTransaction
{
    private BorrowTransaction()
    {
    }

    public Guid BorrowTransactionId { get; private set; }

    public Guid BorrowedItemId { get; private set; }

    public DateOnly BorrowDate { get; private set; }

    public DateOnly? ReturnDate { get; private set; }

    public ReturnCondition? ReturnCondition { get; private set; }

    public decimal? ReturnedQuantity { get; private set; }

    public Guid? RecordedByUserId { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Incident rows carry a condition but no return date; the issue row has neither until returned.</summary>
    public bool IsIssue => ReturnCondition is null || ReturnDate is not null;

    internal static BorrowTransaction Issued(Guid itemId, DateOnly borrowDate, Guid officerUserId, string? note) =>
        new()
        {
            BorrowTransactionId = Guid.NewGuid(),
            BorrowedItemId = itemId,
            BorrowDate = borrowDate,
            RecordedByUserId = officerUserId,
            Note = note
        };

    internal static BorrowTransaction Incident(
        Guid itemId,
        DateOnly borrowDate,
        DateOnly incidentDate,
        ReturnCondition condition,
        decimal quantity,
        Guid officerUserId,
        string note) =>
        new()
        {
            BorrowTransactionId = Guid.NewGuid(),
            BorrowedItemId = itemId,
            BorrowDate = borrowDate,
            ReturnCondition = condition,
            ReturnedQuantity = quantity,
            RecordedByUserId = officerUserId,
            Note = $"{incidentDate:yyyy-MM-dd}: {note}"
        };

    internal void Close(DateOnly returnDate, ReturnCondition condition, decimal quantity, Guid officerUserId, string? note)
    {
        ReturnDate = returnDate;
        ReturnCondition = condition;
        ReturnedQuantity = quantity;
        RecordedByUserId = officerUserId;
        Note = note ?? Note;
    }
}
