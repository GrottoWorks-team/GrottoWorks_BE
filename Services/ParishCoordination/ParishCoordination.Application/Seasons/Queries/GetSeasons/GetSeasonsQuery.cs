namespace ParishCoordination.Application.Seasons.Queries.GetSeasons;

public sealed record GetSeasonsQuery(
    int Page = 1,
    int Size = 20,
    string? Sort = null);
