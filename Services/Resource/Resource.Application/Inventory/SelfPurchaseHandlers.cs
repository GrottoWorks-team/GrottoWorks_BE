using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Contributions;
using Resource.Application.Files;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Purchases;

namespace Resource.Application.Inventory;

public sealed record SelfPurchaseLineDto(
    Guid MaterialId, string MaterialCode, string MaterialName, string Unit, decimal Quantity, decimal UnitPrice, decimal TotalPrice);

/// <summary><c>SelfPurchaseReceiptDto</c>: what the officer recorded, with material names for the UI.</summary>
public sealed record SelfPurchaseReceiptDto(
    Guid Id,
    Guid WorkAreaId,
    Guid SeasonId,
    string BuyerName,
    string? SupplierName,
    DateOnly PurchaseDate,
    decimal TotalCost,
    Guid[] InvoiceFileIds,
    string? Note,
    Guid RecordedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SelfPurchaseLineDto> Items)
{
    public static SelfPurchaseReceiptDto From(SelfPurchaseReceipt receipt, IReadOnlyDictionary<Guid, Material> materials) =>
        new(receipt.SelfPurchaseReceiptId, receipt.WorkAreaId, receipt.SeasonId, receipt.BuyerName, receipt.SupplierName,
            receipt.PurchaseDate, receipt.TotalCost, receipt.InvoiceFileIds, receipt.Note, receipt.RecordedByUserId,
            receipt.CreatedAt,
            receipt.Items.Select(item => materials.TryGetValue(item.MaterialId, out var material)
                ? new SelfPurchaseLineDto(material.MaterialId, material.Code, material.Name, material.Unit, item.Quantity, item.UnitPrice, item.TotalPrice)
                : new SelfPurchaseLineDto(item.MaterialId, string.Empty, string.Empty, string.Empty, item.Quantity, item.UnitPrice, item.TotalPrice)).ToList());
}

public sealed record CreateSelfPurchaseReceiptCommand(
    Guid WorkAreaId,
    string? BuyerName,
    string? SupplierName,
    DateOnly? PurchaseDate,
    IReadOnlyCollection<SelfPurchaseLineInput> Lines,
    IReadOnlyCollection<Guid> InvoiceFileIds,
    string? Note);

/// <summary>
/// "Vật tư người khác tự mua" (26/11/2026): the material officer of the work area records goods bought outside the
/// purchase-request flow. Invoice photos are mandatory (money control), the total is computed server-side, and each line
/// posts a <c>PURCHASE_DELIVERY</c> ledger row referenced by the self-purchase item so stock and cost stay linked.
/// </summary>
public sealed class CreateSelfPurchaseReceiptCommandHandler(
    WorkAreaAccess access,
    IMaterialCatalogStore catalog,
    ISelfPurchaseStore store,
    IInventoryStore inventory,
    ShortageTracker shortages,
    IFileVerifier files,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public Task<SelfPurchaseReceiptDto> HandleAsync(CreateSelfPurchaseReceiptCommand command, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(() => HandleInTransactionAsync(command, cancellationToken), cancellationToken);

    private async Task<SelfPurchaseReceiptDto> HandleInTransactionAsync(
        CreateSelfPurchaseReceiptCommand command, CancellationToken cancellationToken)
    {
        var scope = await access.ForMaterialOfficerWriteAsync(command.WorkAreaId, cancellationToken);

        var materials = (await catalog.GetMaterialsAsync(command.Lines.Select(line => line.MaterialId).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);
        var missing = command.Lines.FirstOrDefault(line => !materials.ContainsKey(line.MaterialId));
        if (missing is not null)
        {
            throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");
        }

        var now = DateTimeOffset.UtcNow;
        var today = ParishClock.Today(now);
        var receipt = SelfPurchaseReceipt.Record(
            scope.WorkAreaId, scope.ParishId, scope.SeasonId, currentUser.UserId,
            command.BuyerName, command.SupplierName, command.PurchaseDate ?? today,
            command.Lines, command.InvoiceFileIds, command.Note, today, now);

        await using var reservation = await files.ReserveAsync(
            receipt.InvoiceFileIds, FilePurposes.PurchaseReceipt, currentUser.UserId, receipt.SelfPurchaseReceiptId, cancellationToken);

        var before = new List<ShortageTracker.Snapshot>();
        foreach (var item in receipt.Items)
        {
            before.Add(await shortages.CaptureAsync(scope, item.MaterialId, cancellationToken));
            inventory.Append(MaterialTransaction.Post(
                scope.WorkAreaId, item.MaterialId, TransactionType.PurchaseDelivery, item.Quantity, now, currentUser.UserId,
                $"Tự mua: {receipt.BuyerName}", TransactionReferenceType.SelfPurchaseItem, item.SelfPurchaseItemId));
        }

        store.Add(receipt);
        await store.SaveChangesAsync(cancellationToken);

        foreach (var snapshot in before)
        {
            await shortages.PublishIfChangedAsync(snapshot, cancellationToken);
        }

        return SelfPurchaseReceiptDto.From(receipt, materials);
    }
}

public sealed record ListSelfPurchaseReceiptsQuery(Guid? WorkAreaId, Guid? SeasonId, Guid? ParishId = null, int Limit = 200);

/// <summary>Reads self-purchase receipts of one work area or one season (PARISH, LEADER, MO of the area).</summary>
public sealed class ListSelfPurchaseReceiptsQueryHandler(
    WorkAreaAccess access,
    ISelfPurchaseStore store,
    IMaterialCatalogStore catalog,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<SelfPurchaseReceiptDto>> HandleAsync(ListSelfPurchaseReceiptsQuery query, CancellationToken cancellationToken = default)
    {
        if (query.WorkAreaId is { } workAreaId)
        {
            await access.ForReadAsync(workAreaId, cancellationToken);
        }
        else if (currentUser.Role is not (BuildingBlocks.Security.GrottoWorksRoles.Parish or BuildingBlocks.Security.GrottoWorksRoles.Admin))
        {
            // Season-wide totals are for the parish committee/admin; a leader or officer only reads their own area.
            throw DomainRuleException.Forbidden("SELF_PURCHASE_SCOPE_REQUIRED", "Pass a work area, or use a parish-level account for season totals.");
        }

        var parishId = access.RequireParish(query.ParishId);
        var limit = Math.Clamp(query.Limit, 1, 500);
        var receipts = await store.ListAsync(parishId, query.WorkAreaId, query.SeasonId, limit, cancellationToken);
        var materials = (await catalog.GetMaterialsAsync(
                receipts.SelectMany(receipt => receipt.Items.Select(item => item.MaterialId)).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);
        return receipts.Select(receipt => SelfPurchaseReceiptDto.From(receipt, materials)).ToList();
    }
}
