using ParishCoordination.Application.WorkAreas;

namespace ParishCoordination.Application.Seasons.Commands.CloneSeason;

/// <summary>
/// Response body of <c>POST /api/v1/seasons/{sourceSeasonId}/clone</c>: everything the client
/// needs to render the new season without a follow-up GET.
/// </summary>
public sealed record CloneSeasonResultDto(
    SeasonDto Season,
    IReadOnlyList<WorkAreaDto> Areas,
    IReadOnlyList<string> Warnings)
{
    public static CloneSeasonResultDto From(CloneSeasonResult result) =>
        new(result.Season!, result.Areas, result.Warnings);
}

public enum CloneSeasonOutcome
{
    Success,
    SourceSeasonNotFound,
    NameConflict,
    YearConflict
}

public sealed record CloneSeasonResult(
    CloneSeasonOutcome Outcome,
    SeasonDto? Season,
    IReadOnlyList<WorkAreaDto> Areas,
    IReadOnlyList<string> Warnings)
{
    public static CloneSeasonResult Success(
        SeasonDto season,
        IReadOnlyList<WorkAreaDto> areas,
        IReadOnlyList<string> warnings) =>
        new(CloneSeasonOutcome.Success, season, areas, warnings);

    public static CloneSeasonResult SourceSeasonNotFound() =>
        new(CloneSeasonOutcome.SourceSeasonNotFound, null, [], []);

    public static CloneSeasonResult NameConflict() =>
        new(CloneSeasonOutcome.NameConflict, null, [], []);

    public static CloneSeasonResult YearConflict() =>
        new(CloneSeasonOutcome.YearConflict, null, [], []);
}
