namespace Resource.Contracts.Events;

/// <summary>
/// <c>support.transfer.confirmed</c> (API Contract v1.3.2 §8, R-22) — publisher Resource (Vũ), consumer TaskExecution
/// (F-TSK-15b: support request PARTIALLY_FULFILLED / FULFILLED). Keyed by <see cref="SupportResponseMaterialId"/> so the
/// consumer can apply it idempotently and reconcile when it arrives late. Quantities are in the material's unit; only
/// <see cref="ReceivedUsableQuantity"/> was posted to the target work area (BR-64).
/// </summary>
public sealed record SupportTransferConfirmed(
    Guid SupportResponseMaterialId,
    Guid SourceWorkAreaId,
    Guid TargetWorkAreaId,
    Guid ParishId,
    Guid MaterialId,
    decimal SentQuantity,
    decimal ReceivedUsableQuantity,
    decimal ReceivedUnusableQuantity,
    Guid ConfirmedByUserId,
    DateTimeOffset ConfirmedAt)
{
    public const string EventType = "support.transfer.confirmed";

    public const int Version = 1;
}
