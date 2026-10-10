using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Seasons;

public interface ISeasonRepository
{
    Task<(IReadOnlyList<Season> Items, long TotalItems)> GetPagedAsync(
        Guid? parishId,
        int page,
        int size,
        string? sort,
        CancellationToken cancellationToken = default);
}
