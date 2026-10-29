namespace Resource.Contracts.Events;

/// <summary>Change of one material line; <see cref="UsableDelta"/> was reconciled with the stock ledger.</summary>
public sealed record DonationAdjustmentLine(Guid DonationItemId, Guid MaterialId, decimal ReceivedDelta, decimal UnusableDelta, decimal UsableDelta);

/// <summary>
/// <c>contribution.donation.adjusted</c> (API Contract v1.3.2 §8, F-RES-14) — publisher Resource (Vũ), consumers Reporting
/// (correct the 1-1 contribution of the donation, F-RPT-02) and NotificationAudit (audit trail with <see cref="Reason"/>).
/// Deltas are corrected minus previously recorded values of one receipt.
/// </summary>
public sealed record ContributionDonationAdjusted(
    Guid DonationId,
    Guid DonationReceiptId,
    Guid DonorId,
    Guid? DonorUserId,
    Guid ParishId,
    Guid SeasonId,
    Guid? WorkAreaId,
    string DonationType,
    decimal AmountDelta,
    string Currency,
    IReadOnlyList<DonationAdjustmentLine> Items,
    string Reason,
    Guid AdjustedByUserId,
    DateTimeOffset AdjustedAt)
{
    public const string EventType = "contribution.donation.adjusted";

    public const int Version = 1;
}
