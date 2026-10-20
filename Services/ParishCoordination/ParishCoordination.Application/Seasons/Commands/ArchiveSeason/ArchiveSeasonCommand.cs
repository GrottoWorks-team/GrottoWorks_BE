namespace ParishCoordination.Application.Seasons.Commands.ArchiveSeason;

public sealed record ArchiveSeasonCommand(
    Guid SeasonId,
    int? ExpectedVersion);
