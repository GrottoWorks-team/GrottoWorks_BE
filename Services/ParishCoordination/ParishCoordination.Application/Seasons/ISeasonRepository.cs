using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Seasons;

public interface ISeasonRepository
{
    Task<bool> ExistsByNameAsync(
        Guid parishId,
        string name,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByYearAsync(
        Guid parishId,
        int seasonYear,
        CancellationToken cancellationToken = default);

    void Add(Season season);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Season> Items, long TotalItems)> GetPagedAsync(
        Guid? parishId,
        int page,
        int size,
        string? sort,
        CancellationToken cancellationToken = default);
}