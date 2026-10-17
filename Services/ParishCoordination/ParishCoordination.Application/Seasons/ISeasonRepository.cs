using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Seasons;

public interface ISeasonRepository
{
    Task<Season?> GetByIdAsync(
        Guid seasonId,
        CancellationToken cancellationToken = default);

    Task<Season?> GetByIdForUpdateAsync(
        Guid seasonId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(
        Guid parishId,
        string name,
        CancellationToken cancellationToken = default,
        Guid? excludeSeasonId = null);

    Task<bool> ExistsByYearAsync(
        Guid parishId,
        int seasonYear,
        CancellationToken cancellationToken = default,
        Guid? excludeSeasonId = null);

    void Add(Season season);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Season> Items, long TotalItems)> GetPagedAsync(
        Guid? parishId,
        int page,
        int size,
        string? sort,
        CancellationToken cancellationToken = default);
}
