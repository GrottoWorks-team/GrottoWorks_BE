namespace ParishCoordination.Application.Parishes.Queries.GetParishes;

public sealed record GetParishesQuery(
    int Page = 1,
    int Size = 20,
    string? Sort = null);
