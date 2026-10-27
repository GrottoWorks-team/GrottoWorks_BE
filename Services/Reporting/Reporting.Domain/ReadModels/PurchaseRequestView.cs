namespace Reporting.Domain.ReadModels;

/// <summary>
/// <c>rm_purchase_request</c> (read model): estimate, decision and actual cost of one purchase request, from
/// <c>purchase.request.submitted/decided</c> and <c>purchase.delivery.confirmed</c>. Status mirrors Resource
/// (PENDING_APPROVAL, APPROVED, REJECTED, PARTIALLY_RECEIVED, COMPLETED).
/// </summary>
public sealed class PurchaseRequestView
{
    private PurchaseRequestView()
    {
    }

    public Guid PurchaseRequestId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public decimal EstimatedTotal { get; private set; }

    public decimal ActualCost { get; private set; }

    public int Deliveries { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Optimistic concurrency token (<c>xmin</c>): concurrent events on the same row → one is redelivered.</summary>
    public uint Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PurchaseRequestView Create(Guid id, Guid workAreaId, Guid seasonId, Guid parishId, Guid requestedBy) =>
        new() { PurchaseRequestId = id, WorkAreaId = workAreaId, SeasonId = seasonId, ParishId = parishId, RequestedByUserId = requestedBy };

    public void Submitted(decimal estimatedTotal, DateTimeOffset at)
    {
        EstimatedTotal = estimatedTotal;
        SubmittedAt = at.ToUniversalTime();
        // A decision or a delivery may have been applied first (events are not ordered across aggregates).
        if (DecidedAt is null && Deliveries == 0)
        {
            Status = "PENDING_APPROVAL";
        }

        UpdatedAt = at.ToUniversalTime();
    }

    public void Decided(string decision, decimal estimatedTotal, DateTimeOffset at)
    {
        EstimatedTotal = estimatedTotal;
        DecidedAt = at.ToUniversalTime();
        if (Deliveries == 0)
        {
            Status = decision.ToUpperInvariant();
        }

        UpdatedAt = at.ToUniversalTime();
    }

    public void Delivered(decimal cost, string requestStatus, DateTimeOffset at)
    {
        ActualCost += cost;
        Deliveries++;
        Status = requestStatus.ToUpperInvariant();
        UpdatedAt = at.ToUniversalTime();
    }
}
