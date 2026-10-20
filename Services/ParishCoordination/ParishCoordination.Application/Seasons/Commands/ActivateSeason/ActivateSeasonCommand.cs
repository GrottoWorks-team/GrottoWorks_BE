namespace ParishCoordination.Application.Seasons.Commands.ActivateSeason;

public sealed record ActivateSeasonCommand(
    Guid SeasonId,
    int? ExpectedVersion);
