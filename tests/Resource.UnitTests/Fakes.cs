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

    public List<Resource.Domain.Inventory.MaterialAllocation> Allocations { get; } = [];

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

    public Task<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>> GetAllocatedTotalsAsync(
        IReadOnlyCollection<Guid> workAreaIds, Guid? materialId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>>(Allocations
            .Where(a => workAreaIds.Contains(a.WorkAreaId))
            .SelectMany(a => a.Items.Select(i => (a.WorkAreaId, i.MaterialId, i.AllocatedQuantity)))
            .Where(row => materialId is null || row.MaterialId == materialId)
            .GroupBy(row => (row.WorkAreaId, row.MaterialId))
            .Select(g => new Resource.Application.Inventory.QuantityByMaterial(g.Key.WorkAreaId, g.Key.MaterialId, g.Sum(row => row.AllocatedQuantity)))
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

public sealed class FakeDonorStore : Resource.Application.Contributions.IDonorStore
{
    public List<Resource.Domain.Contributions.Donor> Donors { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task<Resource.Domain.Contributions.Donor?> GetAsync(Guid donorId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Donors.FirstOrDefault(d => d.DonorId == donorId));

    public Task<Resource.Domain.Contributions.Donor?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Donors.FirstOrDefault(d => d.UserId == userId));

    public Task<IReadOnlyList<Resource.Domain.Contributions.Donor>> GetManyAsync(IReadOnlyCollection<Guid> donorIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Contributions.Donor>>(Donors.Where(d => donorIds.Contains(d.DonorId)).ToList());

    public Task<PagedResult<Resource.Domain.Contributions.Donor>> SearchAsync(
        Resource.Application.Contributions.DonorSearch search, CancellationToken cancellationToken = default)
    {
        var filtered = Donors
            .Where(d => search.ParishId is null || d.ParishId == search.ParishId)
            .Where(d => search.Linked is null || (d.UserId != null) == search.Linked)
            .Where(d => search.Search is null ||
                        d.DonorName.Contains(search.Search, StringComparison.OrdinalIgnoreCase) ||
                        (d.Phone?.Contains(search.Search) ?? false) ||
                        (d.Email?.Contains(search.Search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        IEnumerable<Resource.Domain.Contributions.Donor> ordered = search.SortField == Resource.Application.Contributions.DonorSortField.CreatedAt
            ? filtered.OrderBy(d => d.CreatedAt)
            : filtered.OrderBy(d => d.DonorName, StringComparer.Ordinal);
        if (search.SortDescending)
        {
            ordered = ordered.Reverse();
        }

        return Task.FromResult(new PagedResult<Resource.Domain.Contributions.Donor>(
            ordered.Skip((search.Page - 1) * search.Size).Take(search.Size).ToList(), filtered.Count));
    }

    public void Add(Resource.Domain.Contributions.Donor donor) => Donors.Add(donor);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeIdentityUserLookup : Resource.Application.Users.IIdentityUserLookup
{
    public Dictionary<Guid, Resource.Application.Users.IdentityUser> Users { get; } = [];

    public bool Unavailable { get; set; }

    public Task<Resource.Application.Users.IdentityUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Unavailable
            ? throw new Resource.Application.Users.IdentityUserLookupUnavailableException("down")
            : Task.FromResult(Users.GetValueOrDefault(userId));

    public Resource.Application.Users.IdentityUser Seed(Guid parishId, string role = "VOLUNTEER", string name = "Nguyễn Văn A")
    {
        var user = new Resource.Application.Users.IdentityUser(Guid.NewGuid(), name, "ACTIVE", role, parishId);
        Users[user.UserId] = user;
        return user;
    }
}

public sealed class FakeDonationStore : Resource.Application.Contributions.IDonationStore
{
    public List<Resource.Domain.Contributions.Donation> Donations { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task<Resource.Domain.Contributions.Donation?> GetAsync(Guid donationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Donations.FirstOrDefault(d => d.DonationId == donationId));

    public Task<PagedResult<Resource.Domain.Contributions.Donation>> SearchAsync(
        Resource.Application.Contributions.DonationSearch search, CancellationToken cancellationToken = default)
    {
        var filtered = Donations
            .Where(d => search.SeasonIds is null || search.SeasonIds.Contains(d.SeasonId))
            .Where(d => search.WorkAreaId is null || d.WorkAreaId == search.WorkAreaId)
            .Where(d => search.DonorId is null || d.DonorId == search.DonorId)
            .Where(d => search.Type is null || d.Type == search.Type)
            .Where(d => search.Status is null || d.Status == search.Status)
            .OrderByDescending(d => d.DonationDate)
            .ToList();
        return Task.FromResult(new PagedResult<Resource.Domain.Contributions.Donation>(
            filtered.Skip((search.Page - 1) * search.Size).Take(search.Size).ToList(), filtered.Count));
    }

    public Task<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>> GetOutstandingPledgesAsync(
        IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Application.Inventory.QuantityByMaterial>>(Donations
            .Where(d => d.WorkAreaId is { } area && workAreaIds.Contains(area) &&
                        d.Status is Resource.Domain.Contributions.DonationStatus.Pledged or Resource.Domain.Contributions.DonationStatus.PartiallyReceived)
            .SelectMany(d => d.Items.Where(i => i.OutstandingQuantity > 0).Select(i => (d.WorkAreaId!.Value, i.MaterialId, i.OutstandingQuantity)))
            .GroupBy(row => (row.Item1, row.MaterialId))
            .Select(g => new Resource.Application.Inventory.QuantityByMaterial(g.Key.Item1, g.Key.MaterialId, g.Sum(row => row.OutstandingQuantity)))
            .ToList());

    public void Add(Resource.Domain.Contributions.Donation donation) => Donations.Add(donation);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeTaskProjectionStore : Resource.Application.Scope.ITaskProjectionStore
{
    public List<Resource.Domain.Scope.TaskProjection> Tasks { get; } = [];

    public Task<Resource.Domain.Scope.TaskProjection?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tasks.FirstOrDefault(t => t.TaskId == taskId));

    public Task<IReadOnlyList<Resource.Domain.Scope.TaskProjection>> ListByPurchaseRequestAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Scope.TaskProjection>>(Tasks.Where(t => t.PurchaseRequestId == purchaseRequestId).ToList());

    public void Add(Resource.Domain.Scope.TaskProjection task) => Tasks.Add(task);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Resource.Domain.Scope.TaskProjection Seed(
        Guid workAreaId,
        Resource.Domain.Scope.ProjectedTaskStatus status = Resource.Domain.Scope.ProjectedTaskStatus.InProgress,
        params Guid[] assignees)
    {
        var task = Resource.Domain.Scope.TaskProjection.FromSnapshot(
            new Resource.Domain.Scope.TaskSnapshot(Guid.NewGuid(), workAreaId, "Treo đèn", Resource.Domain.Scope.ProjectedTaskType.Work,
                status, null, assignees, 1),
            DateTimeOffset.UtcNow);
        Tasks.Add(task);
        return task;
    }
}

/// <summary>Allocations live in the inventory fake so balances see them (same unit of work).</summary>
public sealed class FakeAllocationStore(FakeInventoryStore inventory) : Resource.Application.Inventory.IAllocationStore
{
    public Task<PagedResult<Resource.Domain.Inventory.MaterialAllocation>> SearchAsync(
        Resource.Application.Inventory.AllocationSearch search, CancellationToken cancellationToken = default)
    {
        var filtered = inventory.Allocations
            .Where(a => search.WorkAreaIds.Contains(a.WorkAreaId))
            .Where(a => search.TaskId is null || a.TaskId == search.TaskId)
            .Where(a => search.MaterialId is null || a.Items.Any(i => i.MaterialId == search.MaterialId))
            .OrderByDescending(a => a.AllocationDate)
            .ToList();
        return Task.FromResult(new PagedResult<Resource.Domain.Inventory.MaterialAllocation>(
            filtered.Skip((search.Page - 1) * search.Size).Take(search.Size).ToList(), filtered.Count));
    }

    public Task<IReadOnlyList<Resource.Domain.Inventory.MaterialAllocationItem>> ListItemsOfTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Inventory.MaterialAllocationItem>>(
            inventory.Allocations.Where(a => a.TaskId == taskId).SelectMany(a => a.Items).ToList());

    public void Add(Resource.Domain.Inventory.MaterialAllocation allocation) => inventory.Allocations.Add(allocation);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class FakeStockLock : Resource.Application.Abstractions.IWorkAreaStockLock
{
    public int Acquired { get; private set; }

    public int Committed { get; private set; }

    public Task<Resource.Application.Abstractions.IStockLease> AcquireAsync(Guid workAreaId, CancellationToken cancellationToken = default)
    {
        Acquired++;
        return Task.FromResult<Resource.Application.Abstractions.IStockLease>(new Lease(this));
    }

    private sealed class Lease(FakeStockLock owner) : Resource.Application.Abstractions.IStockLease
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            owner.Committed++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class FakeTaskMaterialReportStore : Resource.Application.Inventory.ITaskMaterialReportStore
{
    public List<Resource.Domain.Inventory.TaskMaterialReport> Reports { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task<Resource.Domain.Inventory.TaskMaterialReport?> GetAsync(Guid reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Reports.FirstOrDefault(r => r.TaskMaterialReportId == reportId));

    public Task<Resource.Domain.Inventory.TaskMaterialReport?> GetByTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Reports.FirstOrDefault(r => r.TaskId == taskId));

    public void Add(Resource.Domain.Inventory.TaskMaterialReport report) => Reports.Add(report);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeSupportTransferStore : Resource.Application.Inventory.ISupportTransferStore
{
    public List<Resource.Domain.Inventory.SupportTransfer> Transfers { get; } = [];

    public Task<Resource.Domain.Inventory.SupportTransfer?> GetAsync(Guid supportResponseMaterialId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Transfers.FirstOrDefault(t => t.SupportResponseMaterialId == supportResponseMaterialId));

    public void Add(Resource.Domain.Inventory.SupportTransfer transfer) => Transfers.Add(transfer);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class FakeBorrowedItemStore : Resource.Application.Contributions.IBorrowedItemStore
{
    public List<Resource.Domain.Contributions.BorrowedItem> Items { get; } = [];

    public Task<Resource.Domain.Contributions.BorrowedItem?> GetAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(i => i.BorrowedItemId == itemId));

    public Task<PagedResult<Resource.Domain.Contributions.BorrowedItem>> SearchAsync(
        Resource.Application.Contributions.BorrowedItemSearch search, CancellationToken cancellationToken = default)
    {
        var filtered = Items
            .Where(i => search.SeasonIds.Contains(i.SeasonId) && search.Statuses.Contains(i.Status))
            .Where(i => search.WorkAreaId is null || i.WorkAreaId == search.WorkAreaId)
            .Where(i => search.DueOnOrBefore is null || i.ExpectedReturnDate <= search.DueOnOrBefore)
            .OrderBy(i => i.ExpectedReturnDate)
            .ToList();
        return Task.FromResult(new PagedResult<Resource.Domain.Contributions.BorrowedItem>(
            filtered.Skip((search.Page - 1) * search.Size).Take(search.Size).ToList(), filtered.Count));
    }

    public Task<IReadOnlyList<Resource.Domain.Contributions.BorrowedItem>> ListToRemindAsync(DateOnly dueOnOrBefore, DateOnly today, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Contributions.BorrowedItem>>(Items
            .Where(i => i.IsOut && i.ExpectedReturnDate <= dueOnOrBefore && (i.LastRemindedOn is null || i.LastRemindedOn < today))
            .ToList());

    public void Add(Resource.Domain.Contributions.BorrowedItem item) => Items.Add(item);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class FakePurchaseStore : Resource.Application.Purchases.IPurchaseStore
{
    public List<Resource.Domain.Purchases.Purchase> Purchases { get; } = [];

    public Task<Resource.Domain.Purchases.Purchase?> GetAsync(Guid purchaseId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Purchases.FirstOrDefault(p => p.PurchaseId == purchaseId));

    public Task<IReadOnlyList<Resource.Domain.Purchases.Purchase>> ListByRequestAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Resource.Domain.Purchases.Purchase>>(Purchases.Where(p => p.PurchaseRequestId == purchaseRequestId).ToList());

    public void Add(Resource.Domain.Purchases.Purchase purchase) => Purchases.Add(purchase);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
