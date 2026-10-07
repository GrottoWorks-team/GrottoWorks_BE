using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Parishes;

public sealed class ParishRepository(ParishCoordinationDbContext dbContext)
    : IParishRepository
{
    public async Task<Parish?> GetByIdAsync(
        Guid parishId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Parishes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                parish => parish.Id == parishId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Parish>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Parishes
            .AsNoTracking()
            .OrderBy(parish => parish.Name)
            .ThenBy(parish => parish.Id)
            .ToListAsync(cancellationToken);
    }

    public void Add(Parish parish)
    {
        dbContext.Parishes.Add(parish);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
