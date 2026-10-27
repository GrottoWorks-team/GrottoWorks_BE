namespace Readiness.Application;

public sealed record PageRequest(int Page, int Size);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalItems);
