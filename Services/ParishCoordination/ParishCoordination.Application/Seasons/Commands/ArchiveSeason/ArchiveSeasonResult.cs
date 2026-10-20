namespace ParishCoordination.Application.Seasons.Commands.ArchiveSeason;

public enum ArchiveSeasonOutcome
{
    Success,
    NotFound,
    InvalidState,
    VersionMismatch
}

public sealed record ArchiveSeasonResult(
    ArchiveSeasonOutcome Outcome,
    SeasonDto? Season)
{
    public static ArchiveSeasonResult Success(SeasonDto season) =>
        new(ArchiveSeasonOutcome.Success, season);

    public static ArchiveSeasonResult NotFound() =>
        new(ArchiveSeasonOutcome.NotFound, null);

    public static ArchiveSeasonResult InvalidState() =>
        new(ArchiveSeasonOutcome.InvalidState, null);

    public static ArchiveSeasonResult VersionMismatch() =>
        new(ArchiveSeasonOutcome.VersionMismatch, null);
}
