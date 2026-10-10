using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Communities;

public interface ICommunityRepository
{
    Task<IReadOnlyList<Community>> GetByParishIdAsync(
        Guid parishId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(
        Guid parishId,
        string name,
        CancellationToken cancellationToken = default);

    void Add(Community community);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
