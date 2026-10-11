namespace ParishCoordination.Application.WorkAreas.Queries.ListWorkAreas;

public sealed record ListWorkAreasResult(
    IReadOnlyList<WorkAreaDto> Items,
    long TotalItems);