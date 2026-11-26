using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Contributions;
using Resource.Application.Files;
using Resource.Application.Scope;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Application.Inventory;

public sealed record ParishPurchaseLineDto(
    Guid MaterialId, string MaterialCode, string MaterialName, string Unit, decimal Quantity, decimal UnitPrice, decimal TotalPrice);

/// <summary><c>ParishPurchaseReceiptDto</c>: what the parish recorded, with material names for the UI.</summary>
public sealed record ParishPurchaseReceiptDto(
    Guid Id,
    Guid SeasonId,
    string BuyerName,
    string? SupplierName,
    DateOnly PurchaseDate,
    decimal TotalCost,
    Guid[] InvoiceFileIds,
    string? Note,
    Guid RecordedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ParishPurchaseLineDto> Items)
{
    public static ParishPurchaseReceiptDto From(ParishPurchaseReceipt receipt, IReadOnlyDictionary<Guid, Material> materials) =>
        new(receipt.ParishPurchaseReceiptId, receipt.SeasonId, receipt.BuyerName, receipt.SupplierName, receipt.PurchaseDate,
            receipt.TotalCost, receipt.InvoiceFileIds, receipt.Note, receipt.RecordedByUserId, receipt.CreatedAt,
            receipt.Items.Select(item => materials.TryGetValue(item.MaterialId, out var material)
                ? new ParishPurchaseLineDto(material.MaterialId, material.Code, material.Name, material.Unit, item.Quantity, item.UnitPrice, item.TotalPrice)
                : new ParishPurchaseLineDto(item.MaterialId, string.Empty, string.Empty, string.Empty, item.Quantity, item.UnitPrice, item.TotalPrice)).ToList());
}

public sealed record RecordParishPurchaseCommand(
    Guid SeasonId,
    string? BuyerName,
    string? SupplierName,
    DateOnly? PurchaseDate,
    IReadOnlyCollection<ParishPurchaseLineInput> Lines,
    IReadOnlyCollection<Guid> InvoiceFileIds,
    string? Note);

public sealed record ListParishPurchasesQuery(Guid SeasonId, int Limit = 50, Guid? ParishId = null);

/// <summary>
/// "Vật tư Ban phụ trách đã mua" (26/11/2026): the parish buys the goods itself (no purchase task, no cost estimate),
/// records the receipt with invoice photos and the stock enters the parish warehouse, ready to be distributed down to
/// the work areas that raised the supplementary supply request.
/// </summary>
public sealed class RecordParishPurchaseCommandHandler(
    ParishWarehouseAccess access,
    IMaterialCatalogStore catalog,
    IParishPurchaseStore purchases,
    IParishInventoryStore inventory,
    IFileVerifier files,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public Task<ParishPurchaseReceiptDto> HandleAsync(
        RecordParishPurchaseCommand command,
        CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(() => HandleInTransactionAsync(command, cancellationToken), cancellationToken);

    private async Task<ParishPurchaseReceiptDto> HandleInTransactionAsync(
        RecordParishPurchaseCommand command,
        CancellationToken cancellationToken)
    {
        var (_, parishId) = await access.RequireWritableSeasonAsync(command.SeasonId, cancellationToken);

        var materials = (await catalog.GetMaterialsAsync(
                command.Lines.Select(line => line.MaterialId).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);
        if (command.Lines.FirstOrDefault(line => !materials.ContainsKey(line.MaterialId)) is not null)
        {
            throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");
        }

        if (materials.Values.FirstOrDefault(material => material.Status != CatalogStatus.Active) is { } inactive)
        {
            throw DomainRuleException.Validation("MATERIAL_INACTIVE", $"Material '{inactive.Code}' is inactive.");
        }

        var now = DateTimeOffset.UtcNow;
        var today = ParishClock.Today(now);
        var receipt = ParishPurchaseReceipt.Record(
            parishId, command.SeasonId, currentUser.UserId, command.BuyerName, command.SupplierName,
            command.PurchaseDate ?? today, command.Lines, command.InvoiceFileIds, command.Note, today, now);

        await using var reservation = await files.ReserveAsync(
            receipt.InvoiceFileIds, FilePurposes.PurchaseReceipt, currentUser.UserId, receipt.ParishPurchaseReceiptId, cancellationToken);

        foreach (var item in receipt.Items)
        {
            inventory.Append(ParishMaterialTransaction.Post(
                parishId, command.SeasonId, item.MaterialId, TransactionType.PurchaseDelivery, item.Quantity, now,
                currentUser.UserId, $"Nhập vật tư đã mua (hoá đơn {receipt.PurchaseDate:dd/MM/yyyy})",
                TransactionReferenceType.ParishPurchaseItem, item.ParishPurchaseItemId));
        }

        purchases.Add(receipt);
        await purchases.SaveChangesAsync(cancellationToken);

        foreach (var materialId in receipt.Items.Select(item => item.MaterialId).Distinct())
        {
            await PublishStockAsync(parishId, command.SeasonId, materials[materialId], cancellationToken);
        }

        return ParishPurchaseReceiptDto.From(receipt, materials);
    }

    private async Task PublishStockAsync(Guid parishId, Guid seasonId, Material material, CancellationToken cancellationToken)
    {
        var available = Math.Max(0, (await inventory.GetTotalsAsync(parishId, seasonId, material.MaterialId, cancellationToken))
            .Sum(row => row.Quantity));
        await publisher.PublishAsync(
            ParishMaterialStockChanged.EventType,
            ParishMaterialStockChanged.Version,
            new ParishMaterialStockChanged(parishId, seasonId, material.MaterialId, available, DateTimeOffset.UtcNow,
                material.Code, material.Name, material.Unit),
            "parish_material",
            material.MaterialId,
            cancellationToken: cancellationToken);
    }
}

/// <summary>Latest parish purchase receipts of one season (PARISH; ADMIN names the parish).</summary>
public sealed class ListParishPurchasesQueryHandler(
    ParishWarehouseAccess access,
    IParishPurchaseStore purchases,
    IMaterialCatalogStore catalog)
{
    public async Task<IReadOnlyList<ParishPurchaseReceiptDto>> HandleAsync(
        ListParishPurchasesQuery query,
        CancellationToken cancellationToken = default)
    {
        var parishId = access.RequireReadParish(query.ParishId);
        await access.RequireReadableSeasonAsync(query.SeasonId, parishId, cancellationToken);

        var receipts = await purchases.ListAsync(parishId, query.SeasonId, Math.Clamp(query.Limit, 1, 200), cancellationToken);
        var materials = (await catalog.GetMaterialsAsync(
                receipts.SelectMany(receipt => receipt.Items).Select(item => item.MaterialId).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);

        return receipts.Select(receipt => ParishPurchaseReceiptDto.From(receipt, materials)).ToList();
    }
}
