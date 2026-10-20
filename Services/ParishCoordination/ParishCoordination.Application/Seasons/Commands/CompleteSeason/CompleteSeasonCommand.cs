namespace ParishCoordination.Application.Seasons.Commands.CompleteSeason;

public sealed record CompleteSeasonCommand(
    Guid SeasonId,
    int? ExpectedVersion);
