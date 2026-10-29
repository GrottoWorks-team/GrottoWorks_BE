using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.Application.Inventory;

public sealed record MaterialAllocationItemDto(
    Guid Id,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal AllocatedQuantity);

/// <summary><c>MaterialAllocation</c> (API Contract v1.3.2).</summary>
public sealed record MaterialAllocationDto(
    Guid Id,
    Guid WorkAreaId,
    string WorkAreaName,
    Guid? TaskId,
    string? TaskName,
    Guid AllocatedBy,
    DateTimeOffset AllocationDate,
    string? Note,
    IReadOnlyList<MaterialAllocationItemDto> Items);

public sealed record AllocationSearch(
    IReadOnlyCollection<Guid> WorkAreaIds,
    Guid? TaskId,
    Guid? MaterialId,
    int Page,
    int Size);

public sealed record AllocationFilter(int Page, int Size, Guid? SeasonId, Guid? WorkAreaId, Guid? TaskId, Guid? MaterialId);

public sealed record AllocateMaterialCommand(
    Guid WorkAreaId,
    Guid? TaskId,
    IReadOnlyList<AllocationLineInput> Items,
    DateTimeOffset? AllocationDate,
    string? Note);

public interface IAllocationStore
{
    /// <summary>Newest first, items included.</summary>
    Task<PagedResult<MaterialAllocation>> SearchAsync(AllocationSearch search, CancellationToken cancellationToken = default);

    /// <summary>Allocation items of a task (material report source, BR-45).</summary>
    Task<IReadOnlyList<MaterialAllocationItem>> ListItemsOfTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    void Add(MaterialAllocation allocation);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Builds <see cref="MaterialAllocationDto"/>s with catalog, work-area and task names.</summary>
public sealed class AllocationProjector(IMaterialCatalogStore catalog, IWorkAreaScopeStore scopes, ITaskProjectionStore tasks)
{
    public async Task<IReadOnlyList<MaterialAllocationDto>> ToDtosAsync(IReadOnlyCollection<MaterialAllocation> allocations, CancellationToken cancellationToken)
    {
        var materials = (await catalog.GetMaterialsAsync(
                allocations.SelectMany(a => a.Items).Select(item => item.MaterialId).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);
        var areas = (await scopes.ListByIdsAsync(allocations.Select(a => a.WorkAreaId).Distinct().ToList(), cancellationToken))
            .ToDictionary(area => area.WorkAreaId);
        var taskNames = new Dictionary<Guid, string>();
        foreach (var taskId in allocations.Where(a => a.TaskId is not null).Select(a => a.TaskId!.Value).Distinct())
        {
            if (await tasks.GetAsync(taskId, cancellationToken) is { } task)
            {
                taskNames[taskId] = task.Name;
            }
        }

        return allocations
            .Select(allocation => new MaterialAllocationDto(
                allocation.MaterialAllocationId, allocation.WorkAreaId, areas.GetValueOrDefault(allocation.WorkAreaId)?.Name ?? string.Empty,
                allocation.TaskId, allocation.TaskId is { } id ? taskNames.GetValueOrDefault(id) : null, allocation.AllocatedByUserId,
                allocation.AllocationDate, allocation.Note,
                allocation.Items
                    .Select(item => new MaterialAllocationItemDto(item.MaterialAllocationItemId, item.MaterialId, materials[item.MaterialId].Code,
                        materials[item.MaterialId].Name, materials[item.MaterialId].Unit, item.AllocatedQuantity))
                    .OrderBy(item => item.MaterialName, StringComparer.CurrentCulture)
                    .ToList()))
            .ToList();
    }
}

/// <summary>
/// F-RES-15 <c>allocateMaterial</c> (MO of the work area): hands out confirmed, unallocated stock — to the work area or
/// to one of its open tasks. Under the work-area stock lock every line must fit in the unallocated quantity
/// (BR-33), else 409 <c>MATERIAL_INSUFFICIENT_AVAILABLE</c>; of two officers taking the last units only one succeeds.
/// </summary>
public sealed class AllocateMaterialCommandHandler(
    WorkAreaAccess access,
    ITaskProjectionStore tasks,
    IMaterialCatalogStore catalog,
    InventoryReader reader,
    IAllocationStore store,
    IWorkAreaStockLock stockLock,
    AllocationProjector projector,
    ICurrentUser currentUser)
{
    public async Task<MaterialAllocationDto> HandleAsync(AllocateMaterialCommand command, CancellationToken cancellationToken = default)
    {
        var scope = await access.ForMaterialOfficerWriteAsync(command.WorkAreaId, cancellationToken);
        if (command.TaskId is { } taskId)
        {
            var task = await tasks.GetAsync(taskId, cancellationToken);
            if (task is null || task.WorkAreaId != scope.WorkAreaId)
            {
                throw DomainRuleException.Validation("TASK_UNKNOWN", "The task does not exist in this work area.");
            }

            if (task.IsClosed)
            {
                throw new DomainRuleException("TASK_NOT_ALLOCATABLE", "Materials cannot be allocated to a completed or cancelled task.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var allocation = MaterialAllocation.Create(scope.WorkAreaId, command.TaskId, command.Items, command.AllocationDate ?? now,
            command.Note, currentUser.UserId, now);
        var materials = (await catalog.GetMaterialsAsync(command.Items.Select(item => item.MaterialId).ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);
        if (command.Items.FirstOrDefault(item => !materials.ContainsKey(item.MaterialId)) is { } unknown)
        {
            throw DomainRuleException.Validation("MATERIAL_UNKNOWN", $"Material {unknown.MaterialId} does not exist.");
        }

        await using (var lease = await stockLock.AcquireAsync(scope.WorkAreaId, cancellationToken))
        {
            await StockGuard.EnsureUnallocatedAsync(reader, scope, materials,
                allocation.Items.Select(item => (item.MaterialId, item.AllocatedQuantity)), cancellationToken);
            store.Add(allocation);
            await store.SaveChangesAsync(cancellationToken);
            await lease.CommitAsync(cancellationToken);
        }

        return (await projector.ToDtosAsync([allocation], cancellationToken))[0];
    }
}

/// <summary>F-RES-15 <c>listMaterialAllocations</c> (PARISH, LEADER, MO): allocations of the caller's parish, newest first.</summary>
public sealed class ListMaterialAllocationsQueryHandler(
    WorkAreaAccess access,
    IWorkAreaScopeStore scopes,
    IAllocationStore store,
    AllocationProjector projector)
{
    public async Task<PagedResult<MaterialAllocationDto>> HandleAsync(AllocationFilter filter, CancellationToken cancellationToken = default)
    {
        var areas = (await scopes.ListByParishAsync(access.RequireParish(), filter.SeasonId, cancellationToken))
            .Where(area => filter.WorkAreaId is null || area.WorkAreaId == filter.WorkAreaId)
            .Select(area => area.WorkAreaId)
            .ToList();
        if (areas.Count == 0)
        {
            return new PagedResult<MaterialAllocationDto>([], 0);
        }

        var page = await store.SearchAsync(new AllocationSearch(areas, filter.TaskId, filter.MaterialId, filter.Page, filter.Size), cancellationToken);
        return new PagedResult<MaterialAllocationDto>(await projector.ToDtosAsync(page.Items, cancellationToken), page.TotalItems);
    }
}

/// <summary>BR-33 / Contract 7.1: an outflow of a work area never exceeds its unallocated quantity.</summary>
public static class StockGuard
{
    public static async Task EnsureUnallocatedAsync(
        InventoryReader reader,
        WorkAreaScope scope,
        IReadOnlyDictionary<Guid, Material> materials,
        IEnumerable<(Guid MaterialId, decimal Quantity)> outflows,
        CancellationToken cancellationToken)
    {
        var balances = (await reader.ReadAsync([scope], null, cancellationToken)).ToDictionary(row => row.MaterialId);
        foreach (var (materialId, quantity) in outflows)
        {
            var unallocated = balances.TryGetValue(materialId, out var row) ? row.Unallocated : 0;
            if (quantity > unallocated)
            {
                var material = materials[materialId];
                throw new DomainRuleException(
                    "MATERIAL_INSUFFICIENT_AVAILABLE",
                    $"Only {unallocated} {material.Unit} of {material.Code} is unallocated in this work area; {quantity} was requested.");
            }
        }
    }
}
