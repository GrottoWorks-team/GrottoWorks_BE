namespace Resource.Domain.Scope;

/// <summary>
/// <c>season_scope</c> (projection, spec §2.2): the state of a ParishCoordination season. Kept on its own because the season
/// snapshot can arrive before any of its work areas (Parish delivers the season first), and a work-area snapshot without
/// <c>seasonStatus</c> takes the state from here. Older snapshots (<see cref="SourceVersion"/>) are ignored.
/// </summary>
public sealed class SeasonScope
{
    private SeasonScope()
    {
    }

    public Guid SeasonId { get; private set; }

    /// <summary>
    /// Owning parish. Null only while no snapshot carried it (season events alone do not); it is filled by the first
    /// work-area snapshot or season snapshot and is what lets the parish warehouse reject another parish's season.
    /// </summary>
    public Guid? ParishId { get; private set; }

    public SeasonStatus Status { get; private set; }

    public long SourceVersion { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static SeasonScope Create(Guid seasonId, Guid? parishId, SeasonStatus status, long? version, DateTimeOffset now) =>
        new()
        {
            SeasonId = seasonId,
            ParishId = parishId,
            Status = status,
            SourceVersion = version ?? 0,
            UpdatedAt = now.ToUniversalTime()
        };

    /// <summary>Fills the parish once a snapshot provides it; never overwrites a different value.</summary>
    public void ApplyParish(Guid parishId)
    {
        if (ParishId is null && parishId != Guid.Empty)
        {
            ParishId = parishId;
        }
    }

    /// <summary>
    /// Returns false when <paramref name="version"/> is equal to or older than the stored one. An update without version (manual feed)
    /// applies only while no versioned Parish snapshot has been stored, so it can never roll Parish's state back.
    /// </summary>
    public bool Apply(SeasonStatus status, long? version, DateTimeOffset now)
    {
        if (version is { } value ? value <= SourceVersion : SourceVersion > 0)
        {
            return false;
        }

        Status = status;
        SourceVersion = version ?? SourceVersion;
        UpdatedAt = now.ToUniversalTime();
        return true;
    }
}
