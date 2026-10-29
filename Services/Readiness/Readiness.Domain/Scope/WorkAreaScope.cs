namespace Readiness.Domain.Scope;

/// <summary>Season lifecycle as projected from ParishCoordination (API Contract v1.3, Season.status).</summary>
public enum SeasonStatus
{
    Planning,
    Active,
    Ready,
    Completed,
    Archived
}

/// <summary>Work-area lifecycle as projected from ParishCoordination (API Contract v1.3, WorkArea.status).</summary>
public enum WorkAreaStatus
{
    Planned,
    InProgress,
    ReadinessReview,
    NeedsCorrection,
    Ready,
    Completed
}

/// <summary>
/// Local read-only copy of a ParishCoordination work area (spec §2.2): the work-area scope is not in
/// the JWT, so Readiness checks "leader of this work area", parish tenancy and season state against this projection.
/// Snapshots are applied idempotently; an older <see cref="SourceVersion"/> never overwrites a newer one.
/// </summary>
public sealed class WorkAreaScope
{
    private WorkAreaScope()
    {
    }

    public Guid WorkAreaId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid ParishId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public WorkAreaStatus Status { get; private set; }

    public SeasonStatus SeasonStatus { get; private set; }

    public Guid? CommunityId { get; private set; }

    public Guid? LeaderUserId { get; private set; }

    public Guid? MaterialOfficerUserId { get; private set; }

    /// <summary>Aggregate version of the work area at the source (Parish <c>WorkArea.version</c>).</summary>
    public long SourceVersion { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>BR-07: an archived season is read-only for every service.</summary>
    public bool IsReadOnly => SeasonStatus == SeasonStatus.Archived;

    public static WorkAreaScope FromSnapshot(WorkAreaSnapshot snapshot, DateTimeOffset now)
    {
        var scope = new WorkAreaScope { WorkAreaId = snapshot.WorkAreaId };
        scope.Copy(snapshot, now);
        return scope;
    }

    /// <summary>Returns false (and changes nothing) when the snapshot is older than what is stored.</summary>
    public bool Apply(WorkAreaSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.WorkAreaId != WorkAreaId)
        {
            throw new ArgumentException("Snapshot belongs to another work area.", nameof(snapshot));
        }

        if (snapshot.Version < SourceVersion)
        {
            return false;
        }

        Copy(snapshot, now);
        return true;
    }

    /// <summary>Season events carry no work-area version, so they only move the season state.</summary>
    public void ApplySeasonStatus(SeasonStatus seasonStatus, DateTimeOffset now)
    {
        SeasonStatus = seasonStatus;
        UpdatedAt = now.ToUniversalTime();
    }

    private void Copy(WorkAreaSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.SeasonId == Guid.Empty || snapshot.ParishId == Guid.Empty)
        {
            throw DomainRuleException.Validation("WORK_AREA_SCOPE_INVALID", "Season and parish are required.");
        }

        SeasonId = snapshot.SeasonId;
        ParishId = snapshot.ParishId;
        Name = (snapshot.Name ?? string.Empty).Trim();
        Status = snapshot.Status;
        SeasonStatus = snapshot.SeasonStatus;
        CommunityId = snapshot.CommunityId;
        LeaderUserId = snapshot.LeaderUserId;
        MaterialOfficerUserId = snapshot.MaterialOfficerUserId;
        SourceVersion = snapshot.Version;
        UpdatedAt = now.ToUniversalTime();
    }
}

/// <summary>
/// Input of the projection: what <c>planning.work-area.created/updated/assigned</c> must carry
/// (spec §6.1 "Payload gồm leader/officer/community/category").
/// </summary>
public sealed record WorkAreaSnapshot(
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    string? Name,
    WorkAreaStatus Status,
    SeasonStatus SeasonStatus,
    Guid? CommunityId,
    Guid? LeaderUserId,
    Guid? MaterialOfficerUserId,
    long Version);
