using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    public async Task<bool> ExistsByNameAsync(
        Guid parishId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Communities
            .AsNoTracking()
            .AnyAsync(
                community => community.ParishId == parishId && community.Name == name,
                cancellationToken);
    }

    public void Add(Community community)
    {
        dbContext.Communities.Add(community);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new CommunityNameConflictException(exception);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }
}
