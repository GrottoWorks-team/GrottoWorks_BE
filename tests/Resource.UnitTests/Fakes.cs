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

public sealed class FakeScopeStore : Resource.Application.Scope.IWorkAreaScopeStore
{
    public List<Resource.Domain.Scope.WorkAreaScope> Scopes { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task<Resource.Domain.Scope.WorkAreaScope?> GetAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Scopes.FirstOrDefault(s => s.WorkAreaId == workAreaId));

    public Task<IReadOnlyList<Resource.Domain.Scope.WorkAreaScope>> ListBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Scope.WorkAreaScope>>(Scopes.Where(s => s.SeasonId == seasonId).ToList());

    public Task<IReadOnlyList<Resource.Domain.Scope.WorkAreaScope>> ListByIdsAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Scope.WorkAreaScope>>(Scopes.Where(s => workAreaIds.Contains(s.WorkAreaId)).ToList());

    public Task<IReadOnlyList<Resource.Domain.Scope.WorkAreaScope>> ListByParishAsync(Guid parishId, Guid? seasonId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Scope.WorkAreaScope>>(
            Scopes.Where(s => s.ParishId == parishId && (seasonId is null || s.SeasonId == seasonId)).OrderBy(s => s.Name).ToList());

    public void Add(Resource.Domain.Scope.WorkAreaScope scope) => Scopes.Add(scope);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }

    /// <summary>Adds an ACTIVE work area of an ACTIVE season with the given officer.</summary>
    public Resource.Domain.Scope.WorkAreaScope Seed(
        Guid parishId,
        Guid? materialOfficerId,
        Guid? seasonId = null,
        string name = "Hang đá",
        Resource.Domain.Scope.SeasonStatus seasonStatus = Resource.Domain.Scope.SeasonStatus.Active)
    {
        var scope = Resource.Domain.Scope.WorkAreaScope.FromSnapshot(
            new Resource.Domain.Scope.WorkAreaSnapshot(
                Guid.NewGuid(), seasonId ?? Guid.NewGuid(), parishId, name,
                Resource.Domain.Scope.WorkAreaStatus.InProgress, seasonStatus, null, Guid.NewGuid(), materialOfficerId, 1),
            DateTimeOffset.UtcNow);
        Scopes.Add(scope);
        return scope;
    }
}

public sealed class FakeRequirementStore : Resource.Application.Requirements.IMaterialRequirementStore
{
    public List<Resource.Domain.Inventory.MaterialRequirement> Requirements { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task<IReadOnlyList<Resource.Domain.Inventory.MaterialRequirement>> ListByWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Inventory.MaterialRequirement>>(Requirements.Where(r => r.WorkAreaId == workAreaId).ToList());

    public Task<Resource.Domain.Inventory.MaterialRequirement?> GetAsync(Guid requirementId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Requirements.FirstOrDefault(r => r.MaterialRequirementId == requirementId));

    public Task<bool> ExistsAsync(Guid workAreaId, Guid materialId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Requirements.Any(r => r.WorkAreaId == workAreaId && r.MaterialId == materialId));

    public void Add(Resource.Domain.Inventory.MaterialRequirement requirement) => Requirements.Add(requirement);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}

/// <summary>In-memory ledger; required totals are read from the shared requirement fake (same unit of work).</summary>
public sealed class FakeInventoryStore(FakeRequirementStore requirements) : Resource.Application.Inventory.IInventoryStore
{
    public List<Resource.Domain.Inventory.MaterialTransaction> Ledger { get; } = [];

    public int SaveChangesCount { get; private set; }

    public void Append(Resource.Domain.Inventory.MaterialTransaction transaction) => Ledger.Add(transaction);

    public Task<bool> OpeningStockExistsAsync(Guid workAreaId, Guid materialId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Ledger.Any(t => t.WorkAreaId == workAreaId && t.MaterialId == materialId &&
                                        t.Type == Resource.Domain.Inventory.TransactionType.OpeningStock));

    public Task<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>> GetLedgerTotalsAsync(
        IReadOnlyCollection<Guid> workAreaIds, Guid? materialId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>>(Ledger
            .Where(t => workAreaIds.Contains(t.WorkAreaId) && (materialId is null || t.MaterialId == materialId))
            .GroupBy(t => (t.WorkAreaId, t.MaterialId))
            .Select(g => new Resource.Application.Inventory.QuantityByMaterial(g.Key.WorkAreaId, g.Key.MaterialId, g.Sum(t => t.Quantity)))
            .ToList());

    public Task<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>> GetRequiredTotalsAsync(
        IReadOnlyCollection<Guid> workAreaIds, Guid? materialId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>>(requirements.Requirements
            .Where(r => workAreaIds.Contains(r.WorkAreaId) && r.Status == Resource.Domain.Inventory.RequirementStatus.Active &&
                        (materialId is null || r.MaterialId == materialId))
            .Select(r => new Resource.Application.Inventory.QuantityByMaterial(r.WorkAreaId, r.MaterialId, r.RequiredQuantity))
            .ToList());

    public Task<IReadOnlyList<Resource.Domain.Inventory.MaterialTransaction>> ListTransactionsAsync(
        Resource.Application.Inventory.TransactionQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Inventory.MaterialTransaction>>(Ledger
            .Where(t => t.MaterialId == query.MaterialId && query.WorkAreaIds.Contains(t.WorkAreaId) &&
                        (query.From is null || t.TransactionDate >= query.From) && (query.To is null || t.TransactionDate <= query.To))
            .OrderByDescending(t => t.TransactionDate)
            .Take(query.Limit)
            .ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakePurchaseRequestStore : Resource.Application.Purchases.IPurchaseRequestStore
{
    public List<Resource.Domain.Purchases.PurchaseRequest> Requests { get; } = [];

    public List<Resource.Domain.Purchases.PurchaseRequestStateChange> History { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task<Resource.Domain.Purchases.PurchaseRequest?> GetAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Requests.FirstOrDefault(r => r.PurchaseRequestId == purchaseRequestId));

    public Task<PagedResult<Resource.Domain.Purchases.PurchaseRequest>> SearchAsync(
        Resource.Application.Purchases.PurchaseRequestSearch search, CancellationToken cancellationToken = default)
    {
        var filtered = Requests
            .Where(r => search.WorkAreaIds.Contains(r.WorkAreaId) && (search.Status is null || r.Status == search.Status))
            .ToList();
        var ordered = search.NewestFirst ? filtered.OrderByDescending(r => r.CreatedAt) : filtered.OrderBy(r => r.CreatedAt);
        return Task.FromResult(new PagedResult<Resource.Domain.Purchases.PurchaseRequest>(
            ordered.Skip((search.Page - 1) * search.Size).Take(search.Size).ToList(), filtered.Count));
    }

    public Task<IReadOnlyList<Resource.Domain.Purchases.PurchaseRequestStateChange>> ListStateChangesAsync(
        Guid purchaseRequestId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Purchases.PurchaseRequestStateChange>>(
            History.Where(c => c.PurchaseRequestId == purchaseRequestId).ToList());

    public Task<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>> GetOpenRequestedQuantitiesAsync(
        Guid workAreaId, Guid excludePurchaseRequestId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>>(Requests
            .Where(r => r.WorkAreaId == workAreaId && r.PurchaseRequestId != excludePurchaseRequestId &&
                        Resource.Application.Purchases.PurchaseRequestRules.OpenStatuses.Contains(r.Status))
            .SelectMany(r => r.Items)
            .GroupBy(i => i.MaterialId)
            .Select(g => new Resource.Application.Inventory.QuantityByMaterial(workAreaId, g.Key, g.Sum(i => i.RequestedQuantity)))
            .ToList());

    public void Add(Resource.Domain.Purchases.PurchaseRequest request) => Requests.Add(request);

    /// <summary>Like EF: new state changes of tracked requests are inserted once.</summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        foreach (var change in Requests.SelectMany(r => r.StateChanges).Where(c => !History.Contains(c)))
        {
            History.Add(change);
        }

        return Task.CompletedTask;
    }
}
