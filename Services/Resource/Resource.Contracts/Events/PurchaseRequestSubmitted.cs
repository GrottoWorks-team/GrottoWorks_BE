namespace Resource.Contracts.Events;

/// <summary>One requested line in purchase events. Quantity is in the material's unit; cost in VND (null = not estimated).</summary>
public sealed record PurchaseRequestLine(Guid PurchaseRequestItemId, Guid MaterialId, decimal RequestedQuantity, decimal? EstimatedCost);

/// <summary>
/// <c>purchase.request.submitted</c> (API Contract v1.3 §8, spec §6.1) — publisher Resource (Vũ), consumers Task and
/// NotificationAudit. The request now waits for <see cref="LeaderUserId"/> (leader of the work area, D-01).
/// </summary>
public sealed record PurchaseRequestSubmitted(
    Guid PurchaseRequestId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    Guid RequestedByUserId,
    Guid? LeaderUserId,
    string Reason,
    decimal EstimatedTotal,
    string Currency,
    IReadOnlyList<PurchaseRequestLine> Items,
    DateTimeOffset OccurredAt)
{
    public const string EventType = "purchase.request.submitted";

    public const int Version = 1;
}
