namespace ParishCoordination.Application.Seasons.Commands.CloneSeason;

/// <summary>
/// How work-area dates are carried over into the new season.
/// </summary>
public enum CloneDateShift
{
    /// <summary>Leave area dates empty; the area leader fills them in.</summary>
    None,
    /// <summary>Keep the same offset in days from the season start date.</summary>
    SameDayOfSeason
}

/// <summary>Optional per-area overrides; <c>null</c> keeps the source value.</summary>
public sealed record CloneAreaSelection(
    Guid SourceWorkAreaId,
    string? Name,
    Guid? CategoryId,
    Guid? CommunityId);

/// <summary>
/// F-SEA-xx "Tạo mùa mới từ mùa cũ": one request creates the season plus every
/// selected work area (with date shift and verified assignments) in one transaction.
/// </summary>
public sealed record CloneSeasonCommand(
    Guid SourceSeasonId,
    string Name,
    int SeasonYear,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    decimal? EstimatedBudget,
    CloneDateShift DateShift,
    bool CopyAssignments,
    IReadOnlyList<CloneAreaSelection>? Areas);
