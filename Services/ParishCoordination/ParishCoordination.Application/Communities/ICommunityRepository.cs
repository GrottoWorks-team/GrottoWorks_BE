using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Communities;

public interface ICommunityRepository
{
    Task<IReadOnlyList<Community>> GetByParishIdAsync(
        Guid parishId,
        CancellationToken cancellationToken = default);
}
