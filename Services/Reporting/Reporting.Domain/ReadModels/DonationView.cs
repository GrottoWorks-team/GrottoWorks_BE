namespace Reporting.Domain.ReadModels;

/// <summary>
/// <c>rm_donation</c> (read model): one donation (pledge or direct record) with what was promised and what was actually
/// received, from <c>contribution.pledged</c>, <c>contribution.receipt.confirmed</c> and <c>contribution.donation.adjusted</c>.
/// Money is in VND; material quantities are summed across lines (each line in its material's unit).
/// </summary>
public sealed class DonationView
{
    private DonationView()
    {
    }

    public Guid DonationId { get; private set; }

    public Guid DonorId { get; private set; }

    public Guid? DonorUserId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid? WorkAreaId { get; private set; }

    public string DonationType { get; private set; } = string.Empty;

    public bool Pledged { get; private set; }

    public decimal? PledgedAmount { get; private set; }

    public decimal PledgedQuantity { get; private set; }

    public decimal ReceivedAmount { get; private set; }

    public decimal ReceivedUsableQuantity { get; private set; }

    public decimal ReceivedUnusableQuantity { get; private set; }

    public int Receipts { get; private set; }

    public DateTimeOffset? FirstReceivedAt { get; private set; }

    /// <summary>Optimistic concurrency token (<c>xmin</c>): concurrent events on the same row → one is redelivered.</summary>
    public uint Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>PLEDGED (nothing received), PARTIALLY_RECEIVED (less than promised) or RECEIVED.</summary>
    public string Status =>
        Receipts == 0 ? "PLEDGED"
        : !Pledged ? "RECEIVED"
        : DonationType == "MONEY"
            ? (ReceivedAmount >= (PledgedAmount ?? 0) ? "RECEIVED" : "PARTIALLY_RECEIVED")
            : (ReceivedUsableQuantity + ReceivedUnusableQuantity >= PledgedQuantity ? "RECEIVED" : "PARTIALLY_RECEIVED");

    public static DonationView Create(Guid donationId, Guid donorId, Guid? donorUserId, Guid seasonId, Guid parishId, Guid? workAreaId, string type) =>
        new()
        {
            DonationId = donationId,
            DonorId = donorId,
            DonorUserId = donorUserId,
            SeasonId = seasonId,
            ParishId = parishId,
            WorkAreaId = workAreaId,
            DonationType = type.ToUpperInvariant()
        };

    public void Pledge(decimal? amount, decimal quantity, DateTimeOffset at)
    {
        Pledged = true;
        PledgedAmount = amount;
        PledgedQuantity = quantity;
        UpdatedAt = at.ToUniversalTime();
    }

    public void Receive(Guid? donorUserId, decimal amount, decimal usable, decimal unusable, DateTimeOffset at)
    {
        DonorUserId = donorUserId ?? DonorUserId;
        ReceivedAmount += amount;
        ReceivedUsableQuantity += usable;
        ReceivedUnusableQuantity += unusable;
        Receipts++;
        FirstReceivedAt ??= at.ToUniversalTime();
        UpdatedAt = at.ToUniversalTime();
    }

    public void Adjust(decimal amountDelta, decimal usableDelta, decimal unusableDelta, DateTimeOffset at)
    {
        ReceivedAmount += amountDelta;
        ReceivedUsableQuantity += usableDelta;
        ReceivedUnusableQuantity += unusableDelta;
        UpdatedAt = at.ToUniversalTime();
    }
}
