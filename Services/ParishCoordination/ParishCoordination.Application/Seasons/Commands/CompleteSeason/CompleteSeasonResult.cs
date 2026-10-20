namespace ParishCoordination.Application.Seasons.Commands.CompleteSeason;

public enum CompleteSeasonOutcome
{
    Success,
    NotFound,
    InvalidState,
    VersionMismatch
}

public sealed record CompleteSeasonResult(
    CompleteSeasonOutcome Outcome,
    SeasonDto? Season)
{
    public static CompleteSeasonResult Success(SeasonDto season) =>
        new(CompleteSeasonOutcome.Success, season);

    public static CompleteSeasonResult NotFound() =>
        new(CompleteSeasonOutcome.NotFound, null);

    public static CompleteSeasonResult InvalidState() =>
        new(CompleteSeasonOutcome.InvalidState, null);

    public static CompleteSeasonResult VersionMismatch() =>
        new(CompleteSeasonOutcome.VersionMismatch, null);
}
