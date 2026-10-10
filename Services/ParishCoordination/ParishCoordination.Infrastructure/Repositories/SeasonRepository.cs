using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Repositories;

public sealed class SeasonRepository(ParishCoordinationDbContext dbContext)
    : ISeasonRepository
{
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
}
