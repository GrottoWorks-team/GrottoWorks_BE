namespace Resource.Contracts.Events;

/// <summary>
/// <c>loan.offer.submitted</c> (API Contract v1.3.2 §8) — publisher Resource (Vũ), consumer NotificationAudit: tell the
/// material officers of the season (or of <see cref="WorkAreaId"/>) that a loan offer waits for a decision.
/// </summary>
public sealed record LoanOfferSubmitted(
    Guid BorrowedItemId,
    Guid ParishId,
    Guid SeasonId,
    Guid? WorkAreaId,
    IReadOnlyList<Guid> MaterialOfficerUserIds,
    Guid OfferedByUserId,
    string ItemName,
    decimal Quantity,
    string OwnerName,
    DateOnly ExpectedReturnDate,
    DateTimeOffset OfferedAt)
{
    public const string EventType = "loan.offer.submitted";

    public const int Version = 1;
}

/// <summary>
/// <c>borrowed-item.return-due</c> / <c>borrowed-item.overdue</c> (API Contract v1.3.2 §8) — publisher Resource (Vũ, daily
/// reminder job), consumer NotificationAudit. <see cref="OfferedByUserId"/> is the volunteer who entered the offer and
/// is the contact with the lender (D-05); <see cref="MaterialOfficerUserIds"/> handle the return.
/// </summary>
public sealed record BorrowedItemReminder(
    Guid BorrowedItemId,
    Guid ParishId,
    Guid SeasonId,
    Guid? WorkAreaId,
    Guid? TaskId,
    IReadOnlyList<Guid> MaterialOfficerUserIds,
    Guid OfferedByUserId,
    string ItemName,
    string OwnerName,
    string? OwnerPhone,
    DateOnly ExpectedReturnDate,
    int DaysOverdue)
{
    public const string ReturnDueEventType = "borrowed-item.return-due";

    public const string OverdueEventType = "borrowed-item.overdue";

    public const int Version = 1;
}
