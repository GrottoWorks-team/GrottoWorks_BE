namespace Reporting.Domain.ReadModels;

/// <summary>
/// <c>rm_readiness_area</c> (read model): readiness of one work area from <c>readiness.*</c> events. Failed counts are the
/// failures reported since the area was last confirmed READY (Readiness publishes no event for a passed recheck).
/// </summary>
public sealed class ReadinessAreaView
{
    private ReadinessAreaView()
    {
    }

    public Guid WorkAreaId { get; private set; }

    public Guid ReadinessChecklistId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid ParishId { get; private set; }

    /// <summary>NEEDS_CORRECTION, READY or REOPENED.</summary>
    public string Status { get; private set; } = string.Empty;

    public int FailedItems { get; private set; }

    public int FailedMandatoryItems { get; private set; }

    public int ReopenCount { get; private set; }

    public DateTimeOffset? ReadyAt { get; private set; }

    /// <summary>Optimistic concurrency token (<c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ReadinessAreaView Create(Guid workAreaId, Guid checklistId, Guid seasonId, Guid parishId) =>
        new() { WorkAreaId = workAreaId, ReadinessChecklistId = checklistId, SeasonId = seasonId, ParishId = parishId };

    public void ItemFailed(bool mandatory, DateTimeOffset at)
    {
        FailedItems++;
        FailedMandatoryItems += mandatory ? 1 : 0;
        if (mandatory)
        {
            Status = "NEEDS_CORRECTION";
        }
        else if (Status.Length == 0)
        {
            Status = "IN_REVIEW";
        }

        UpdatedAt = at.ToUniversalTime();
    }

    public void Ready(DateTimeOffset at)
    {
        Status = "READY";
        FailedItems = 0;
        FailedMandatoryItems = 0;
        ReadyAt = at.ToUniversalTime();
        UpdatedAt = at.ToUniversalTime();
    }

    public void Reopened(DateTimeOffset at)
    {
        Status = "REOPENED";
        ReopenCount++;
        ReadyAt = null;
        UpdatedAt = at.ToUniversalTime();
    }
}

/// <summary><c>rm_readiness_history</c> (read model): checklist and corrective-action history for the readiness report.</summary>
public sealed class ReadinessHistoryEntry
{
    private ReadinessHistoryEntry()
    {
    }

    public Guid ReadinessHistoryEntryId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public Guid ReadinessChecklistId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid ParishId { get; private set; }

    /// <summary>ITEM_FAILED, AREA_READY or AREA_REOPENED.</summary>
    public string Kind { get; private set; } = string.Empty;

    public string? ItemName { get; private set; }

    public bool? Mandatory { get; private set; }

    public string? Note { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static ReadinessHistoryEntry Record(
        Guid workAreaId,
        Guid checklistId,
        Guid seasonId,
        Guid parishId,
        string kind,
        string? itemName,
        bool? mandatory,
        string? note,
        Guid actorUserId,
        DateTimeOffset at) =>
        new()
        {
            ReadinessHistoryEntryId = Guid.NewGuid(),
            WorkAreaId = workAreaId,
            ReadinessChecklistId = checklistId,
            SeasonId = seasonId,
            ParishId = parishId,
            Kind = kind,
            ItemName = itemName,
            Mandatory = mandatory,
            Note = note,
            ActorUserId = actorUserId,
            OccurredAt = at.ToUniversalTime()
        };
}
