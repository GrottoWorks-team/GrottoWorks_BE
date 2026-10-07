using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Parishes;

public interface IParishRepository
{
    Task<bool> ExistsAsync(
        Guid parishId,
        CancellationToken cancellationToken = default);

    Task<Parish?> GetByIdAsync(
        Guid parishId,
        CancellationToken cancellationToken = default);

    Task<Parish?> GetByIdForUpdateAsync(
        Guid parishId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Parish>> GetAllAsync(
        CancellationToken cancellationToken = default);

    void Add(Parish parish);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
