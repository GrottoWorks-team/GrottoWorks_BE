namespace ParishCoordination.Application.Seasons.Commands.CreateSeason;

public sealed record CreateSeasonCommand(
    Guid? RequestedParishId,
    string Name,
    int SeasonYear,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    decimal? EstimatedBudget);