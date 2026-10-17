namespace ParishCoordination.Application.Seasons.Commands.UpdateSeason;

public sealed record UpdateSeasonCommand(
    Guid SeasonId,
    string? Name,
    int? SeasonYear,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool ShouldUpdateDescription,
    string? Description,
    decimal? EstimatedBudget,
    int? ExpectedVersion);
