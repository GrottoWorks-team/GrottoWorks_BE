using BuildingBlocks.Security;
using ParishCoordination.Application.Communities;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.UnitTests;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; init; }
    public Guid UserId { get; init; } = Guid.NewGuid();
    public string Role { get; init; } = GrottoWorksRoles.Parish;
    public Guid? ParishId { get; init; }
    public Guid? CommunityId { get; init; }
}

internal sealed class FakeParishRepository : IParishRepository
{
    public List<Parish> Items { get; } = [];
    public bool ThrowConcurrencyOnSave { get; set; }
    public Guid? LastScopeId { get; private set; }

    public Task<bool> ExistsAsync(Guid parishId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Any(parish => parish.Id == parishId));

    public Task<Parish?> GetByIdAsync(Guid parishId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.SingleOrDefault(parish => parish.Id == parishId));

    public Task<Parish?> GetByIdForUpdateAsync(Guid parishId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.SingleOrDefault(parish => parish.Id == parishId));

    public Task<(IReadOnlyList<Parish> Items, long TotalItems)> GetPagedAsync(
        int page,
        int size,
        bool sortDescending,
        Guid? parishId = null,
        CancellationToken cancellationToken = default)
    {
        LastScopeId = parishId;
        IEnumerable<Parish> query = Items;
        if (parishId.HasValue)
        {
            query = query.Where(parish => parish.Id == parishId.Value);
        }

        query = sortDescending
            ? query.OrderByDescending(parish => parish.Name).ThenByDescending(parish => parish.Id)
            : query.OrderBy(parish => parish.Name).ThenBy(parish => parish.Id);

        var all = query.ToList();
        return Task.FromResult<(IReadOnlyList<Parish>, long)>(
            (all.Skip((page - 1) * size).Take(size).ToList(), all.Count));
    }

    public void Add(Parish parish) => Items.Add(parish);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowConcurrencyOnSave)
        {
            throw new ParishConcurrencyException(new InvalidOperationException("simulated concurrency"));
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeCommunityRepository : ICommunityRepository
{
    public List<Community> Items { get; } = [];

    public Task<IReadOnlyList<Community>> GetByParishIdAsync(
        Guid parishId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Community>>(
            Items.Where(community => community.ParishId == parishId).ToList());

    public Task<bool> ExistsByNameAsync(
        Guid parishId,
        string name,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Any(community =>
            community.ParishId == parishId &&
            community.Name == name));

    public void Add(Community community) => Items.Add(community);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class FakeSeasonRepository : ISeasonRepository
{
    public List<Season> Items { get; } = [];
    public Guid? LastScopeId { get; private set; }

    public Task<(IReadOnlyList<Season> Items, long TotalItems)> GetPagedAsync(
        Guid? parishId,
        int page,
        int size,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        LastScopeId = parishId;
        IEnumerable<Season> query = Items;

        if (parishId.HasValue)
        {
            query = query.Where(season => season.ParishId == parishId.Value);
        }

        var sortParts = (sort ?? "seasonYear,desc").Split(',');
        var field = sortParts[0];
        var descending = sortParts.Length == 2 &&
            string.Equals(sortParts[1], "desc", StringComparison.OrdinalIgnoreCase);

        query = field switch
        {
            "name" => descending
                ? query.OrderByDescending(season => season.Name).ThenByDescending(season => season.Id)
                : query.OrderBy(season => season.Name).ThenBy(season => season.Id),
            "seasonYear" => descending
                ? query.OrderByDescending(season => season.SeasonYear).ThenByDescending(season => season.StartDate).ThenByDescending(season => season.Id)
                : query.OrderBy(season => season.SeasonYear).ThenBy(season => season.StartDate).ThenBy(season => season.Id),
            "startDate" => descending
                ? query.OrderByDescending(season => season.StartDate).ThenByDescending(season => season.Id)
                : query.OrderBy(season => season.StartDate).ThenBy(season => season.Id),
            "endDate" => descending
                ? query.OrderByDescending(season => season.EndDate).ThenByDescending(season => season.Id)
                : query.OrderBy(season => season.EndDate).ThenBy(season => season.Id),
            "status" => descending
                ? query.OrderByDescending(season => season.Status).ThenByDescending(season => season.Id)
                : query.OrderBy(season => season.Status).ThenBy(season => season.Id),
            _ => query.OrderByDescending(season => season.SeasonYear).ThenByDescending(season => season.StartDate).ThenByDescending(season => season.Id)
        };

        var all = query.ToList();
        return Task.FromResult<(IReadOnlyList<Season>, long)>(
            (all.Skip((page - 1) * size).Take(size).ToList(), all.Count));
    }
}
