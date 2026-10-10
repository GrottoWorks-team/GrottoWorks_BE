using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Repositories;

public sealed class SeasonRepository(ParishCoordinationDbContext dbContext)
    : ISeasonRepository
{
    public async Task<bool> ExistsByNameAsync(
        Guid parishId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Seasons
            .AsNoTracking()
            .AnyAsync(
                season => season.ParishId == parishId && season.Name == name,
                cancellationToken);
    }

    public async Task<bool> ExistsByYearAsync(
        Guid parishId,
        int seasonYear,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Seasons
            .AsNoTracking()
            .AnyAsync(
                season => season.ParishId == parishId && season.SeasonYear == seasonYear,
                cancellationToken);
    }

    public void Add(Season season)
    {
        dbContext.Seasons.Add(season);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryGetUniqueConflict(exception, out var conflict))
        {
            throw conflict == SeasonConflict.Name
                ? new SeasonNameConflictException(exception)
                : new SeasonYearConflictException(exception);
        }
    }

    public async Task<(IReadOnlyList<Season> Items, long TotalItems)> GetPagedAsync(
        Guid? parishId,
        int page,
        int size,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        var seasons = dbContext.Seasons
            .AsNoTracking()
            .AsQueryable();

        if (parishId.HasValue)
        {
            seasons = seasons.Where(season => season.ParishId == parishId.Value);
        }

        var sortParts = (sort ?? "seasonYear,desc").Split(',');
        var field = sortParts[0];
        var descending = sortParts.Length == 2 &&
            string.Equals(sortParts[1], "desc", StringComparison.OrdinalIgnoreCase);

        seasons = field switch
        {
            "name" => descending
                ? seasons.OrderByDescending(season => season.Name).ThenByDescending(season => season.Id)
                : seasons.OrderBy(season => season.Name).ThenBy(season => season.Id),
            "seasonYear" => descending
                ? seasons.OrderByDescending(season => season.SeasonYear).ThenByDescending(season => season.StartDate).ThenByDescending(season => season.Id)
                : seasons.OrderBy(season => season.SeasonYear).ThenBy(season => season.StartDate).ThenBy(season => season.Id),
            "startDate" => descending
                ? seasons.OrderByDescending(season => season.StartDate).ThenByDescending(season => season.Id)
                : seasons.OrderBy(season => season.StartDate).ThenBy(season => season.Id),
            "endDate" => descending
                ? seasons.OrderByDescending(season => season.EndDate).ThenByDescending(season => season.Id)
                : seasons.OrderBy(season => season.EndDate).ThenBy(season => season.Id),
            "status" => descending
                ? seasons.OrderByDescending(season => season.Status).ThenByDescending(season => season.Id)
                : seasons.OrderBy(season => season.Status).ThenBy(season => season.Id),
            _ => seasons.OrderByDescending(season => season.SeasonYear).ThenByDescending(season => season.StartDate).ThenByDescending(season => season.Id)
        };

        var totalItems = await seasons.LongCountAsync(cancellationToken);
        var items = await seasons
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return (items, totalItems);
    }

    private static bool TryGetUniqueConflict(
        DbUpdateException exception,
        out SeasonConflict conflict)
    {
        conflict = default;
        if (exception.InnerException is not PostgresException postgresException ||
            postgresException.SqlState != PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }

        var constraint = postgresException.ConstraintName ?? string.Empty;
        if (constraint.Contains("season_name", StringComparison.OrdinalIgnoreCase))
        {
            conflict = SeasonConflict.Name;
            return true;
        }

        if (constraint.Contains("season_year", StringComparison.OrdinalIgnoreCase))
        {
            conflict = SeasonConflict.Year;
            return true;
        }

        return false;
    }

    private enum SeasonConflict
    {
        Name,
        Year
    }
}