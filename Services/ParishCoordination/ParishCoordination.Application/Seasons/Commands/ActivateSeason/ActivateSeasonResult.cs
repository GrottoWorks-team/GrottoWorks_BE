namespace ParishCoordination.Application.Seasons.Commands.ActivateSeason;

public enum ActivateSeasonOutcome
{
    Success,
    NotFound,
    InvalidState,
    WorkAreaRequired,
    WorkAreaAssignmentIncomplete,
    ActivePeriodConflict,
    VersionMismatch
}

public sealed record ActivateSeasonResult(
    ActivateSeasonOutcome Outcome,
    SeasonDto? Season)
{
    public static ActivateSeasonResult Success(SeasonDto season) =>
        new(ActivateSeasonOutcome.Success, season);

    public static ActivateSeasonResult NotFound() =>
        new(ActivateSeasonOutcome.NotFound, null);

    public static ActivateSeasonResult InvalidState() =>
        new(ActivateSeasonOutcome.InvalidState, null);

    public static ActivateSeasonResult WorkAreaRequired() =>
        new(ActivateSeasonOutcome.WorkAreaRequired, null);

    public static ActivateSeasonResult WorkAreaAssignmentIncomplete() =>
        new(ActivateSeasonOutcome.WorkAreaAssignmentIncomplete, null);

    public static ActivateSeasonResult ActivePeriodConflict() =>
        new(ActivateSeasonOutcome.ActivePeriodConflict, null);

    public static ActivateSeasonResult VersionMismatch() =>
        new(ActivateSeasonOutcome.VersionMismatch, null);
}
