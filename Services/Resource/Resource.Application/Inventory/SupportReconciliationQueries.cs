using Resource.Application.Scope;
using Resource.Contracts.Inventory;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Application.Inventory;

public sealed record AvailableMaterialTotals(Guid MaterialId,CatalogStatus Status,decimal Ledger,decimal Allocated);
public interface ISupportReconciliationReadStore
{
    Task<IReadOnlyList<SupportTransferSnapshot>> GetTransfersAsync(IReadOnlyCollection<Guid> ids,CancellationToken cancellationToken);
    Task<IReadOnlyList<AvailableMaterialTotals>> GetAvailabilityAsync(Guid workAreaId,IReadOnlyCollection<Guid> materialIds,CancellationToken cancellationToken);
}

public sealed class SupportReconciliationQueries(ISupportReconciliationReadStore store,IWorkAreaScopeStore scopes,TimeProvider clock)
{
    public async Task<IReadOnlyList<SupportTransferSnapshot>> TransfersAsync(IReadOnlyCollection<Guid> ids,CancellationToken ct=default)
    {
        var normalized=ValidateIds(ids);
        var snapshots=await store.GetTransfersAsync(normalized,ct);
        if(snapshots.Any(row=>row.ParishId==Guid.Empty || row.SeasonId==Guid.Empty))
            throw new DomainRuleException("RESOURCE_SCOPE_UNAVAILABLE","Transfer ownership scope is not available for reconciliation.",DomainErrorKind.ServiceUnavailable);
        var byId=snapshots.ToDictionary(row=>row.SupportResponseMaterialId);
        return normalized.Where(byId.ContainsKey).Select(id=>byId[id]).ToList();
    }

    public async Task<IReadOnlyList<AvailableSupportMaterial>> AvailabilityAsync(Guid workAreaId,IReadOnlyCollection<Guid> materialIds,CancellationToken ct=default)
    {
        var ids=ValidateIds(materialIds);
        var scope=await scopes.GetAsync(workAreaId,ct)
            ?? throw DomainRuleException.NotFound("WORK_AREA_NOT_FOUND","The work area is not projected in Resource.");
        var totals=await store.GetAvailabilityAsync(workAreaId,ids,ct);
        var now=clock.GetUtcNow();var byId=totals.ToDictionary(row=>row.MaterialId);
        return ids.Where(byId.ContainsKey).Select(id=>
        {
            var row=byId[id];
            var figures=InventoryCalculator.Calculate(0,row.Ledger,row.Allocated);
            return new AvailableSupportMaterial(scope.WorkAreaId,scope.ParishId,scope.SeasonId,scope.MaterialOfficerUserId,scope.SourceVersion,
                id,row.Status==CatalogStatus.Active ? "ACTIVE" : "INACTIVE",figures.Unallocated,now);
        }).ToList();
    }

    private static Guid[] ValidateIds(IReadOnlyCollection<Guid> ids)
    {
        if(ids.Count is <1 or >100 || ids.Any(id=>id==Guid.Empty))
            throw new DomainRuleException("INVALID_IDS","Supply 1–100 non-empty UUIDs.",DomainErrorKind.BadRequest);
        return ids.Distinct().ToArray();
    }
}
