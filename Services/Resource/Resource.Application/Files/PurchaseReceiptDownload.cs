using BuildingBlocks.Security;
using Resource.Application.Purchases;
using Resource.Domain;

namespace Resource.Application.Files;

public sealed class PurchaseReceiptDownload(PurchaseContext context, ICurrentUser user, IRecordFileDownload files)
{
    public async Task<RecordFileDownload> GetAsync(Guid purchaseId, Guid fileId, CancellationToken ct)
    {
        var (purchase, _, scope) = await context.LoadAsync(purchaseId, ct);
        var allowed = user.Role == GrottoWorksRoles.Volunteer && purchase.BuyerUserId == user.UserId ||
            user.Role == GrottoWorksRoles.Leader && scope.LeaderUserId == user.UserId ||
            user.Role == GrottoWorksRoles.MaterialOfficer && scope.MaterialOfficerUserId == user.UserId ||
            user.Role == GrottoWorksRoles.Parish && user.ParishId == scope.ParishId;
        if (!allowed || !purchase.ReceiptFileIds.Contains(fileId)) throw PurchaseContext.NotFound();
        return await files.GetAsync(fileId, purchaseId, ct);
    }
}
