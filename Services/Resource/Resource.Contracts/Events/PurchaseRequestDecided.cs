namespace Resource.Contracts.Events;

/// <summary>
/// <c>purchase.request.decided</c> (API Contract v1.3 §8, spec §6.1) — publisher Resource (Vũ), consumers Task and
/// NotificationAudit. <see cref="Decision"/> is <c>APPROVED</c> or <c>REJECTED</c>; Task only lets the leader create a
/// PURCHASE task for an APPROVED request (BR-28, F-TSK-17). <see cref="Note"/> is the rejection reason (BR-31) or the
/// optional approval note.
/// </summary>
public sealed record PurchaseRequestDecided(
    Guid PurchaseRequestId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    Guid RequestedByUserId,
    string Decision,
    Guid DecidedByUserId,
    string? Note,
    decimal EstimatedTotal,
    string Currency,
    IReadOnlyList<PurchaseRequestLine> Items,
    DateTimeOffset DecidedAt)
{
    public const string EventType = "purchase.request.decided";

    public const int Version = 1;

    public const string Approved = "APPROVED";

    public const string Rejected = "REJECTED";
}
