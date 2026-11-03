using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Resource.Application.Purchases;
using Resource.Contracts.Purchases;
using Resource.Domain.Purchases;
using Resource.Domain.Scope;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Purchases;

public sealed class InternalPurchaseReadStore(ResourceDbContext db) : IInternalPurchaseReadStore
{
    public async Task<InternalPurchaseRequestSnapshot?> FindAsync(Guid id, CancellationToken ct)
    {
        var request = await db.PurchaseRequests.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.PurchaseRequestId == id, ct);
        if (request is null) return null;
        var scope = await db.WorkAreaScopes.AsNoTracking().FirstOrDefaultAsync(x => x.WorkAreaId == request.WorkAreaId, ct)
            ?? throw new Resource.Domain.DomainRuleException("WORK_AREA_NOT_PROJECTED", "Purchase request scope is unavailable.", Resource.Domain.DomainErrorKind.ServiceUnavailable);
        var purchases = await db.Purchases.AsNoTracking().Where(x => x.PurchaseRequestId == id).OrderBy(x => x.PurchaseId)
            .Select(x => new InternalPurchaseDelivery(x.PurchaseId, x.TaskId, x.Status.ToString(), x.ConfirmedAt, x.Version,
                x.ConfirmedByUserId,x.BuyerUserId)).ToListAsync(ct);
        return new(request.PurchaseRequestId, request.PurchaseRequestId, scope.WorkAreaId, scope.SeasonId, scope.ParishId,
            Code(request.Status), request.Version, request.ApprovedByUserId, request.ApprovedAt,
            request.Items.OrderBy(x => x.PurchaseRequestItemId).Select(x => new InternalPurchaseRequestLine(x.PurchaseRequestItemId,
                x.MaterialId, x.RequestedQuantity, x.EstimatedCost)).ToList(),
            purchases.Select(x => x with { Status = JsonNamingPolicy.SnakeCaseUpper.ConvertName(x.Status) }).ToList());
    }

    public async Task<InternalPurchaseRequestPage> ListAsync(Guid? afterId, int limit, CancellationToken ct)
    {
        var query = db.PurchaseRequests.AsNoTracking();
        if (afterId.HasValue) query = query.Where(x => x.PurchaseRequestId.CompareTo(afterId.Value) > 0);
        var ids = await query.OrderBy(x => x.PurchaseRequestId).Take(limit + 1).Select(x => x.PurchaseRequestId).ToListAsync(ct);
        var rows = new List<InternalPurchaseRequestSnapshot>();
        foreach (var id in ids.Take(limit)) rows.Add((await FindAsync(id, ct))!);
        return new(rows, ids.Count > limit ? rows[^1].Id : null);
    }

    public async Task<InternalTaskMaterialSummary?> MaterialSummaryAsync(Guid taskId, CancellationToken ct)
    {
        var task = await db.TaskProjections.AsNoTracking().FirstOrDefaultAsync(x => x.TaskId == taskId, ct);
        if (task is null) return null; // Missing source ownership is not an empty material list.
        var plan = await db.Set<TaskMaterialPlan>().AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.TaskId == taskId, ct);
        var allocations = await db.MaterialAllocations.AsNoTracking().Include(x => x.Items).Where(x => x.TaskId == taskId).ToListAsync(ct);
        var report = await db.TaskMaterialReports.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.TaskId == taskId, ct);
        var items = allocations.SelectMany(x => x.Items).ToDictionary(x => x.MaterialAllocationItemId);
        var verified = report?.Status == Resource.Domain.Inventory.MaterialReportStatus.Verified;
        var materials = (plan?.Items.Select(x => x.MaterialId) ?? []).Concat(items.Values.Select(x => x.MaterialId)).Distinct().Order();
        var lines = new List<InternalTaskMaterialLine>();
        foreach (var materialId in materials)
        {
            var allocated = items.Values.Where(x => x.MaterialId == materialId).Sum(x => x.AllocatedQuantity);
            var reported = report?.Items.Where(x => items.TryGetValue(x.MaterialAllocationItemId, out var allocation) && allocation.MaterialId == materialId).ToList() ?? [];
            var remaining = reported.Sum(x => x.VerifiedRemaining ?? 0);
            var damaged = reported.Sum(x => x.VerifiedDamaged ?? 0);
            var lost = reported.Sum(x => x.VerifiedLost ?? 0);
            lines.Add(new(materialId, plan?.Items.FirstOrDefault(x => x.MaterialId == materialId)?.RequiredQuantity ?? 0,
                allocated, verified ? allocated - remaining - damaged - lost : null,
                verified ? remaining : null, verified ? damaged : null, verified ? lost : null));
        }
        return new(taskId, task.WorkAreaId, plan?.SourceVersion, report is null ? null : Code(report.Status), lines);
    }

    private static string Code<T>(T value) where T : Enum => JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());
}
