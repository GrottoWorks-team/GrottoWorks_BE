namespace ParishCoordination.Application.Parishes.Queries.GetParishes;

public sealed record GetParishesResult(
    IReadOnlyList<ParishDto> Items,
    long TotalItems);
