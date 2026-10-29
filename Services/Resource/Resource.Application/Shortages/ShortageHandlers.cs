using Resource.Application.Abstractions;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Scope;

namespace Resource.Application.Shortages;

/// <summary><c>MaterialShortage</c>: one required material of a work area with its calculated figures.</summary>
public sealed record MaterialShortageDto(
    Guid WorkAreaId,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal Required,
    decimal ConfirmedAvailable,
    decimal Allocated,
    decimal Unallocated,
    decimal Shortage)
{
    public static MaterialShortageDto From(InventoryBalanceDto row) =>
        new(row.WorkAreaId, row.MaterialId, row.MaterialCode, row.MaterialName, row.Unit, row.Required,
            row.ConfirmedAvailable, row.Allocated, row.Unallocated, row.Shortage);
}

/// <summary>
/// <c>MaterialNeed</c>: a positive shortage shown to contributors. Stock and allocation details are left out —
/// volunteers only need what is still missing.
/// </summary>
public sealed record MaterialNeedDto(
    Guid WorkAreaId,
    string WorkAreaName,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal Required,
    decimal Shortage)
{
    public static MaterialNeedDto From(InventoryBalanceDto row) =>
        new(row.WorkAreaId, row.WorkAreaName, row.MaterialId, row.MaterialCode, row.MaterialName, row.Unit, row.Required, row.Shortage);
}

public sealed record MaterialNeedFilter(int Page, int Size, Guid SeasonId, Guid? WorkAreaId = null, Guid? MaterialId = null);

/// <summary>
/// F-RES-05 <c>getWorkAreaShortages</c> (PARISH, LEADER, MO): every ACTIVE requirement line of the work area
/// with its shortage (0 when covered), biggest shortage first.
/// </summary>
public sealed class GetWorkAreaShortagesQueryHandler(WorkAreaAccess access, InventoryReader reader)
{
    public async Task<IReadOnlyList<MaterialShortageDto>> HandleAsync(Guid workAreaId, CancellationToken cancellationToken = default)
    {
        var scope = await access.ForReadAsync(workAreaId, cancellationToken);
        var rows = await reader.ReadAsync([scope], null, cancellationToken);

        return rows
            .Where(row => row.Required > 0)
            .OrderByDescending(row => row.Shortage)
            .ThenBy(row => row.MaterialName, StringComparer.CurrentCulture)
            .Select(MaterialShortageDto.From)
            .ToList();
    }
}

/// <summary>
/// F-RES-05 <c>listMaterialNeeds</c> (USER): positive shortages of a season in the caller's parish, for
/// contribution and pledges (D-02). Season of another parish → empty list (nothing to disclose).
/// </summary>
public sealed class ListMaterialNeedsQueryHandler(WorkAreaAccess access, IWorkAreaScopeStore scopes, InventoryReader reader)
{
    public async Task<PagedResult<MaterialNeedDto>> HandleAsync(MaterialNeedFilter filter, CancellationToken cancellationToken = default)
    {
        var parishId = access.RequireParish();
        var areas = (await scopes.ListByParishAsync(parishId, filter.SeasonId, cancellationToken))
            .Where(area => filter.WorkAreaId is null || area.WorkAreaId == filter.WorkAreaId)
            .ToList();

        var needs = (await reader.ReadAsync(areas, filter.MaterialId, cancellationToken))
            .Where(row => row.Shortage > 0)
            .OrderByDescending(row => row.Shortage)
            .ThenBy(row => row.WorkAreaName, StringComparer.CurrentCulture)
            .ThenBy(row => row.MaterialName, StringComparer.CurrentCulture)
            .Select(MaterialNeedDto.From)
            .ToList();

        return new PagedResult<MaterialNeedDto>(
            needs.Skip((filter.Page - 1) * filter.Size).Take(filter.Size).ToList(),
            needs.Count);
    }
}

/// <summary>
/// Publishes <c>material.shortage.changed</c> when a write changed the shortage of a (work area, material).
/// Usage: <c>var before = await tracker.CaptureAsync(...)</c> → write + save → <c>await tracker.PublishIfChangedAsync(before)</c>.
/// </summary>
public sealed class ShortageTracker(InventoryReader reader, IIntegrationEventPublisher publisher)
{
    public sealed record Snapshot(WorkAreaScope Scope, Guid MaterialId, decimal Shortage, InventoryBalanceDto? Row);

    public async Task<Snapshot> CaptureAsync(WorkAreaScope scope, Guid materialId, CancellationToken cancellationToken = default)
    {
        var row = await ReadAsync(scope, materialId, cancellationToken);
        return new Snapshot(scope, materialId, row?.Shortage ?? 0, row);
    }

    public async Task PublishIfChangedAsync(Snapshot before, CancellationToken cancellationToken = default)
    {
        var row = await ReadAsync(before.Scope, before.MaterialId, cancellationToken);
        var shortage = row?.Shortage ?? 0;
        if (shortage == before.Shortage)
        {
            return;
        }

        await publisher.PublishAsync(
            MaterialShortageChanged.EventType,
            MaterialShortageChanged.Version,
            new MaterialShortageChanged(
                before.Scope.WorkAreaId,
                before.Scope.SeasonId,
                before.Scope.ParishId,
                before.MaterialId,
                before.Shortage,
                shortage,
                row?.Required ?? 0,
                row?.ConfirmedAvailable ?? 0,
                DateTimeOffset.UtcNow,
                (row ?? before.Row)?.MaterialCode ?? string.Empty,
                (row ?? before.Row)?.MaterialName ?? string.Empty,
                (row ?? before.Row)?.Unit ?? string.Empty),
            "work_area_material",
            before.Scope.WorkAreaId,
            cancellationToken);
    }

    private async Task<InventoryBalanceDto?> ReadAsync(WorkAreaScope scope, Guid materialId, CancellationToken cancellationToken) =>
        (await reader.ReadAsync([scope], materialId, cancellationToken)).SingleOrDefault();
}
