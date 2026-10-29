using Resource.Application.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.Application.Inventory;

/// <summary>
/// Builds <see cref="InventoryBalanceDto"/> rows for a set of work areas: every (work area, material) that has an
/// ACTIVE requirement or ledger rows, computed with <see cref="InventoryCalculator"/>. Shared by listInventory,
/// shortages and material needs so the formula lives in one place.
/// </summary>
public sealed class InventoryReader(IInventoryStore inventory, IMaterialCatalogStore catalog)
{
    public async Task<IReadOnlyList<InventoryBalanceDto>> ReadAsync(
        IReadOnlyCollection<WorkAreaScope> scopes,
        Guid? materialId,
        CancellationToken cancellationToken = default)
    {
        if (scopes.Count == 0)
        {
            return [];
        }

        var workAreaIds = scopes.Select(scope => scope.WorkAreaId).ToList();
        var ledger = (await inventory.GetLedgerTotalsAsync(workAreaIds, materialId, cancellationToken))
            .ToDictionary(row => (row.WorkAreaId, row.MaterialId), row => row.Quantity);
        var required = (await inventory.GetRequiredTotalsAsync(workAreaIds, materialId, cancellationToken))
            .ToDictionary(row => (row.WorkAreaId, row.MaterialId), row => row.Quantity);

        var allocated = (await inventory.GetAllocatedTotalsAsync(workAreaIds, materialId, cancellationToken))
            .ToDictionary(row => (row.WorkAreaId, row.MaterialId), row => row.Quantity);

        var keys = ledger.Keys.Union(required.Keys).Union(allocated.Keys).ToList();
        var materials = (await catalog.GetMaterialsAsync(keys.Select(key => key.MaterialId).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);
        var scopeById = scopes.ToDictionary(scope => scope.WorkAreaId);

        return keys
            .Select(key => InventoryBalanceDto.From(
                scopeById[key.WorkAreaId],
                materials[key.MaterialId],
                InventoryCalculator.Calculate(required.GetValueOrDefault(key), ledger.GetValueOrDefault(key), allocated.GetValueOrDefault(key))))
            .OrderBy(row => row.WorkAreaName, StringComparer.CurrentCulture)
            .ThenBy(row => row.MaterialName, StringComparer.CurrentCulture)
            .ThenBy(row => row.MaterialId)
            .ToList();
    }
}
