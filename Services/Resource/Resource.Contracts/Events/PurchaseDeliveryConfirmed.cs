namespace Resource.Contracts.Events;

public sealed record PurchaseDeliveryLine(Guid MaterialId, decimal PurchasedQuantity, decimal ReceivedUsableQuantity, decimal ReceivedUnusableQuantity);

/// <summary>
/// <c>purchase.delivery.confirmed</c> (API Contract v1.3.2 §8) — publisher Resource (Vũ), consumers TaskExecution (the
/// PURCHASE task <see cref="TaskId"/> completes automatically, BR-65 / F-TSK-17), Reporting (purchase cost, buyer
/// contribution) and NotificationAudit. Only <c>ReceivedUsableQuantity</c> reached the stock (BR-26).
/// <see cref="RequestStatus"/> is COMPLETED or PARTIALLY_RECEIVED (short delivery, the request stays open).
/// </summary>
public sealed record PurchaseDeliveryConfirmed(
    Guid PurchaseId,
    Guid PurchaseRequestId,
    Guid TaskId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    Guid BuyerUserId,
    Guid ConfirmedByUserId,
    decimal TotalCost,
    string Currency,
    string RequestStatus,
    IReadOnlyList<PurchaseDeliveryLine> Items,
    DateTimeOffset ConfirmedAt)
{
    public const string EventType = "purchase.delivery.confirmed";

    public const int Version = 1;
}
