using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreaCategories;

public interface IWorkAreaCategoryRepository
{
    Task<IReadOnlyList<WorkAreaCategory>> GetAllAsync(
        WorkAreaCategoryStatus? status,
        CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(
        string normalizedCode,
        CancellationToken cancellationToken = default);

    Task<WorkAreaCategory?> GetByIdAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default);

    Task<WorkAreaCategory?> GetByIdForUpdateAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default);

    void Add(WorkAreaCategory category);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
