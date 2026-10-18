using BuildingBlocks.Security;
using Resource.Application;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Domain.Catalog;

namespace Resource.UnitTests;

public sealed class FakeCatalogStore : IMaterialCatalogStore
{
    public List<MaterialCategory> Categories { get; } = [];

    public List<Material> Materials { get; } = [];

    public HashSet<Guid> ReferencedMaterials { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task<IReadOnlyList<MaterialCategory>> ListCategoriesAsync(CatalogStatus? status, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaterialCategory>>(Categories.Where(c => status is null || c.Status == status).OrderBy(c => c.Name).ToList());

    public Task<MaterialCategory?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Categories.FirstOrDefault(c => c.MaterialCategoryId == categoryId));

    public Task<bool> CategoryCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Categories.Any(c => c.Code == normalizedCode));

    public Task<bool> CategoryHasActiveMaterialsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Materials.Any(m => m.MaterialCategoryId == categoryId && m.Status == CatalogStatus.Active));

    public void AddCategory(MaterialCategory category) => Categories.Add(category);

    public Task<PagedResult<Material>> SearchMaterialsAsync(MaterialSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var filtered = Materials
            .Where(m => criteria.CategoryId is null || m.MaterialCategoryId == criteria.CategoryId)
            .Where(m => criteria.Status is null || m.Status == criteria.Status)
            .Where(m => criteria.Search is null ||
                        m.Name.Contains(criteria.Search, StringComparison.OrdinalIgnoreCase) ||
                        m.Code.Contains(criteria.Search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        IEnumerable<Material> ordered = criteria.SortField == MaterialSortField.Code
            ? (criteria.SortDescending ? filtered.OrderByDescending(m => m.Code) : filtered.OrderBy(m => m.Code))
            : (criteria.SortDescending ? filtered.OrderByDescending(m => m.Name) : filtered.OrderBy(m => m.Name));
        var page = ordered.Skip((criteria.Page - 1) * criteria.Size).Take(criteria.Size).ToList();
        return Task.FromResult(new PagedResult<Material>(page, filtered.Count));
    }

    public Task<Material?> GetMaterialAsync(Guid materialId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Materials.FirstOrDefault(m => m.MaterialId == materialId));

    public Task<IReadOnlyList<Material>> GetMaterialsAsync(IReadOnlyCollection<Guid> materialIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Material>>(Materials.Where(m => materialIds.Contains(m.MaterialId)).ToList());

    public Task<bool> MaterialCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Materials.Any(m => m.Code == normalizedCode));

    public Task<bool> IsMaterialReferencedAsync(Guid materialId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReferencedMaterials.Contains(materialId));

    public void AddMaterial(Material material) => Materials.Add(material);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }

    public MaterialCategory SeedCategory(string code = "LIGHT", string name = "Lighting")
    {
        var category = MaterialCategory.Create(code, name, null);
        Categories.Add(category);
        return category;
    }

    public Material SeedMaterial(MaterialCategory category, string code = "LED-01", string name = "LED string", string unit = "m")
    {
        var material = Material.Create(category, code, name, unit, null);
        Materials.Add(material);
        return material;
    }
}

public sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    public sealed record Published(string EventType, int Version, object Payload, string AggregateType, Guid AggregateId);

    public List<Published> Events { get; } = [];

    public Task PublishAsync<TPayload>(string eventType, int version, TPayload payload, string aggregateType, Guid aggregateId, CancellationToken cancellationToken = default)
        where TPayload : class
    {
        Events.Add(new Published(eventType, version, payload, aggregateType, aggregateId));
        return Task.CompletedTask;
    }
}

public sealed class FakeCurrentUser(Guid userId, string role, Guid? parishId = null) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId { get; } = userId;

    public string Role { get; } = role;

    public Guid? ParishId { get; } = parishId;

    public Guid? CommunityId => null;
}
