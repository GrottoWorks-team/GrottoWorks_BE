using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Parishes;

public interface IParishRepository
{
    Task<IReadOnlyList<Parish>> GetAllAsync(
        CancellationToken cancellationToken = default);

    void Add(Parish parish);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
