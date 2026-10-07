using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.Communities;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Repositories;

public sealed class CommunityRepository(ParishCoordinationDbContext dbContext)
    : ICommunityRepository
{
    public async Task<IReadOnlyList<Community>> GetByParishIdAsync(
        Guid parishId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Communities
            .AsNoTracking()
            .Where(community => community.ParishId == parishId)
            .ToListAsync(cancellationToken);
    }
}
