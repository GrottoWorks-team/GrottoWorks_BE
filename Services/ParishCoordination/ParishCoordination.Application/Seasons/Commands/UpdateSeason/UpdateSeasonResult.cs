namespace ParishCoordination.Application.Seasons.Commands.UpdateSeason;

public enum UpdateSeasonOutcome
{
    Success,
    NotFound,
    InvalidState,
    InvalidDateRange,
    NameConflict,
    YearConflict,
    VersionMismatch
}

public sealed record UpdateSeasonResult(
    UpdateSeasonOutcome Outcome,
    SeasonDto? Season)
{
    public static UpdateSeasonResult Success(SeasonDto season) =>
        new(UpdateSeasonOutcome.Success, season);

    public static UpdateSeasonResult NotFound() =>
        new(UpdateSeasonOutcome.NotFound, null);

    public static UpdateSeasonResult InvalidState() =>
        new(UpdateSeasonOutcome.InvalidState, null);

    public static UpdateSeasonResult InvalidDateRange() =>
        new(UpdateSeasonOutcome.InvalidDateRange, null);

    public static UpdateSeasonResult NameConflict() =>
        new(UpdateSeasonOutcome.NameConflict, null);

    public static UpdateSeasonResult YearConflict() =>
        new(UpdateSeasonOutcome.YearConflict, null);

    public static UpdateSeasonResult VersionMismatch() =>
        new(UpdateSeasonOutcome.VersionMismatch, null);
}
