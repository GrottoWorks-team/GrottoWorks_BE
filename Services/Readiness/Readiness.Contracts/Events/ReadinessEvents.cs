namespace Readiness.Contracts.Events;

/// <summary>
/// <c>readiness.item.failed</c> (API Contract v1.3.2 §8) — publisher Readiness (Vũ), consumers NotificationAudit and
/// Reporting. A mandatory failure blocks the area (BR-52) and needs a CORRECTIVE task (BR-66).
/// </summary>
public sealed record ReadinessItemFailed(
    Guid ReadinessChecklistId,
    Guid ReadinessItemId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    Guid? LeaderUserId,
    string ItemName,
    bool Mandatory,
    string? Note,
    Guid CheckedByUserId,
    DateTimeOffset CheckedAt)
{
    public const string EventType = "readiness.item.failed";

    public const int Version = 1;
}

/// <summary>
/// <c>readiness.area.ready</c> (API Contract v1.3.2 §8) — publisher Readiness (Vũ), consumers ParishCoordination (work
/// area → READY, F-PAR-09), NotificationAudit and Reporting.
/// </summary>
public sealed record ReadinessAreaReady(
    Guid ReadinessChecklistId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    Guid ConfirmedByUserId,
    DateTimeOffset ConfirmedAt)
{
    public const string EventType = "readiness.area.ready";

    public const int Version = 1;
}

/// <summary>
/// <c>readiness.area.reopened</c> (API Contract v1.3.2 §8) — publisher Readiness (Vũ), consumers ParishCoordination (work
/// area back to NEEDS_CORRECTION / review), NotificationAudit and Reporting. <see cref="Reason"/> is required (BR-53).
/// </summary>
public sealed record ReadinessAreaReopened(
    Guid ReadinessChecklistId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    string Reason,
    Guid ReopenedByUserId,
    DateTimeOffset ReopenedAt)
{
    public const string EventType = "readiness.area.reopened";

    public const int Version = 1;
}
