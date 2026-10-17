using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreaCategories;

public interface IWorkAreaCategoryRepository
{
    Task<IReadOnlyList<WorkAreaCategory>> GetAllAsync(
        WorkAreaCategoryStatus? status,
        CancellationToken cancellationToken = default);
}
