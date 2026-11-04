using Microsoft.EntityFrameworkCore;
using Resource.Application.Inventory;
using Resource.Contracts.Inventory;
using Resource.Domain.Inventory;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Inventory;

public sealed class SupportReconciliationReadStore(ResourceDbContext db) : ISupportReconciliationReadStore
{
    public async Task<IReadOnlyList<SupportTransferSnapshot>> GetTransfersAsync(IReadOnlyCollection<Guid> ids,CancellationToken ct)=>
        await (from transfer in db.SupportTransfers.AsNoTracking()
               join scope in db.WorkAreaScopes on transfer.TargetWorkAreaId equals scope.WorkAreaId into targets
               from target in targets.DefaultIfEmpty()
               where ids.Contains(transfer.SupportResponseMaterialId)
               select new SupportTransferSnapshot(transfer.SupportResponseMaterialId,transfer.SourceWorkAreaId,transfer.TargetWorkAreaId,
                   target==null ? Guid.Empty : target.ParishId,target==null ? Guid.Empty : target.SeasonId,transfer.MaterialId,transfer.Quantity,
                   transfer.Status==SupportTransferStatus.Received ? "RECEIVED" : "IN_TRANSIT",transfer.ApprovedByUserId,transfer.TransferredAt,
                   transfer.ReceivedUsableQuantity,transfer.ReceivedUnusableQuantity,transfer.ReceivedConfirmedByUserId,transfer.ReceivedConfirmedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AvailableMaterialTotals>> GetAvailabilityAsync(Guid areaId,IReadOnlyCollection<Guid> ids,CancellationToken ct)=>
        await db.Materials.AsNoTracking().Where(material=>ids.Contains(material.MaterialId)).Select(material=>new AvailableMaterialTotals(
            material.MaterialId,material.Status,
            db.MaterialTransactions.Where(row=>row.WorkAreaId==areaId && row.MaterialId==material.MaterialId).Sum(row=>(decimal?)row.Quantity) ?? 0,
            db.MaterialAllocations.Where(allocation=>allocation.WorkAreaId==areaId).SelectMany(allocation=>allocation.Items)
                .Where(item=>item.MaterialId==material.MaterialId).Sum(item=>(decimal?)item.AllocatedQuantity) ?? 0)).ToListAsync(ct);
}
