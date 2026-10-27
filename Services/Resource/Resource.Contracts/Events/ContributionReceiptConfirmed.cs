namespace Resource.Contracts.Events;

/// <summary>One material line of a receipt. Only <see cref="UsableQuantity"/> was posted to stock (BR-38).</summary>
public sealed record ContributionReceiptLine(
    Guid DonationItemId,
    Guid MaterialId,
    decimal ReceivedQuantity,
    decimal UnusableQuantity,
    decimal UsableQuantity);

/// <summary>
/// <c>contribution.receipt.confirmed</c> (API Contract v1.3.2 §8, spec §6.1) — publisher Resource (Vũ), consumers
/// Reporting (contribution + recognition, F-RPT-02) and NotificationAudit. Published for <b>every</b> receipt, including
/// the one a direct donation creates (<see cref="IsDirect"/>). <see cref="DonorUserId"/> is null for external donors,
/// who earn no recognition points. Money is in VND; <see cref="ReceivedAmount"/> is null for MATERIAL.
/// </summary>
public sealed record ContributionReceiptConfirmed(
    Guid DonationId,
    Guid DonationReceiptId,
    Guid DonorId,
    Guid? DonorUserId,
    Guid ParishId,
    Guid SeasonId,
    Guid? WorkAreaId,
    string DonationType,
    decimal? ReceivedAmount,
    string Currency,
    IReadOnlyList<ContributionReceiptLine> Items,
    bool IsDirect,
    bool IsFirstReceipt,
    Guid RecordedByUserId,
    DateTimeOffset ReceivedAt)
{
    public const string EventType = "contribution.receipt.confirmed";

    public const int Version = 1;
}
