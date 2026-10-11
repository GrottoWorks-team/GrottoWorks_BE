namespace ParishCoordination.Application.WorkAreas.Queries.ListWorkAreas;

public sealed record ListWorkAreasQuery(
    Guid SeasonId,
    int Page,
    int Size,
    string? Sort);