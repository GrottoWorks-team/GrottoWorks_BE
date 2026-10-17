using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.WorkAreas;

public interface IWorkAreaRepository
{
    void Add(WorkArea workArea);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<WorkArea> Items, long TotalItems)> GetPagedBySeasonIdAsync(
        Guid seasonId,
        int page,
        int size,
        string? sort,
        CancellationToken cancellationToken = default);

    Task<WorkArea?> GetByIdAsync(
        Guid workAreaId,
        CancellationToken cancellationToken = default);

    Task<WorkArea?> GetByIdForUpdateAsync(
        Guid workAreaId,
        CancellationToken cancellationToken = default);
}
