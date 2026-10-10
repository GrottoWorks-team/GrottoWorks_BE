using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Repositories;

public sealed class ParishRepository(ParishCoordinationDbContext dbContext)
    : IParishRepository
{
    public async Task<bool> ExistsAsync(
        Guid parishId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Parishes
            .AsNoTracking()
            .AnyAsync(
                parish => parish.Id == parishId,
                cancellationToken);
    }

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

    public async Task<(IReadOnlyList<Parish> Items, long TotalItems)> GetPagedAsync(
        int page,
        int size,
        bool sortDescending,
        Guid? parishId = null,
        CancellationToken cancellationToken = default)
    {
        var parishes = dbContext.Parishes.AsNoTracking();

        if (parishId.HasValue)
        {
            parishes = parishes.Where(parish => parish.Id == parishId.Value);
        }

        parishes = sortDescending
            ? parishes.OrderByDescending(parish => parish.Name).ThenByDescending(parish => parish.Id)
            : parishes.OrderBy(parish => parish.Name).ThenBy(parish => parish.Id);

        var totalItems = await parishes.LongCountAsync(cancellationToken);
        var items = await parishes
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return (items, totalItems);
    }

    public async Task<Parish?> GetByIdForUpdateAsync(
        Guid parishId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Parishes
            .SingleOrDefaultAsync(
                parish => parish.Id == parishId,
                cancellationToken);
    }

    public void Add(Parish parish)
    {
        dbContext.Parishes.Add(parish);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ParishConcurrencyException(exception);
        }
    }
}
