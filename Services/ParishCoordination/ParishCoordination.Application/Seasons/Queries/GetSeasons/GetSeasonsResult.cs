namespace ParishCoordination.Application.Seasons.Queries.GetSeasons;

public sealed record GetSeasonsResult(
    IReadOnlyList<SeasonDto> Items,
    long TotalItems);
