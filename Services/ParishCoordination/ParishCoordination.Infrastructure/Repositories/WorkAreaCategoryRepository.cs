using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParishCoordination.Application.WorkAreaCategories;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Repositories;

public sealed class WorkAreaCategoryRepository(
    ParishCoordinationDbContext dbContext)
    : IWorkAreaCategoryRepository
{
    public async Task<IReadOnlyList<WorkAreaCategory>> GetAllAsync(
        WorkAreaCategoryStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.WorkAreaCategories.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(category => category.Status == status.Value);
        }

        return await query
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> CodeExistsAsync(
        string normalizedCode,
        CancellationToken cancellationToken = default)
    {
        return dbContext.WorkAreaCategories
            .AsNoTracking()
            .AnyAsync(
                category => category.Code == normalizedCode,
                cancellationToken);
    }

    public Task<WorkAreaCategory?> GetByIdForUpdateAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.WorkAreaCategories
            .SingleOrDefaultAsync(
                category => category.Id == categoryId,
                cancellationToken);
    }

    public Task<WorkAreaCategory?> GetByIdAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.WorkAreaCategories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                category => category.Id == categoryId,
                cancellationToken);
    }

    public void Add(WorkAreaCategory category)
    {
        dbContext.WorkAreaCategories.Add(category);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new WorkAreaCategoryCodeConflictException(exception);
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
