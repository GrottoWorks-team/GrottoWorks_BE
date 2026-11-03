using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Support;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Support;

public sealed class SupportMaterialSyncStore(TaskExecutionDbContext db) : ISupportMaterialSyncStore
{
    public Task<Guid?> FindRequestIdAsync(Guid itemId, CancellationToken ct) => db.Set<SupportResponseMaterial>()
        .Where(item=>item.Id==itemId).Select(item=>(Guid?)item.RequestId).SingleOrDefaultAsync(ct);
    public async Task<IReadOnlyList<Guid>> GetPendingItemsAsync(Guid? after, int size, CancellationToken ct) =>
        await db.Set<SupportResponseMaterial>().AsNoTracking()
            .Where(item=>item.Status==SupportMaterialStatus.Pending && item.ApprovalOperationId!=null && item.ApprovalErrorCode==null &&
                (!after.HasValue || item.Id.CompareTo(after.Value)>0))
            .OrderBy(item=>item.Id).Take(size).Select(item=>item.Id).ToListAsync(ct);
}
