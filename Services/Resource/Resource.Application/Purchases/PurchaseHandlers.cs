using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Contributions;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Purchases;
using Resource.Domain.Scope;

namespace Resource.Application.Purchases;

public sealed record PurchaseItemDto(
    Guid Id,
    Guid PurchaseRequestItemId,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal PurchasedQuantity,
    MoneyDto UnitPrice,
    MoneyDto TotalPrice,
    decimal? ReceivedUsableQuantity,
    decimal? ReceivedUnusableQuantity);

/// <summary><c>Purchase</c> (API Contract v1.3.2). <see cref="CostOverrun"/> = actual cost above the remaining estimate.</summary>
public sealed record PurchaseDto(
    Guid Id,
    Guid PurchaseRequestId,
    Guid TaskId,
    Guid BuyerUserId,
    string? SupplierName,
    DateOnly PurchaseDate,
    MoneyDto TotalCost,
    PurchaseStatus Status,
    bool? CostOverrun,
    Guid? CostOverrunApprovedBy,
    DateTimeOffset? CostOverrunApprovedAt,
    Guid? ConfirmedBy,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<Guid> ReceiptFileIds,
    string? InfoRequestReason,
    IReadOnlyList<PurchaseItemDto> Items,
    long Version)
{
    public static PurchaseDto From(Purchase purchase, IReadOnlyDictionary<Guid, Material> materials, decimal? remainingEstimate) =>
        new(purchase.PurchaseId, purchase.PurchaseRequestId, purchase.TaskId, purchase.BuyerUserId, purchase.SupplierName, purchase.PurchaseDate,
            MoneyDto.InVnd(purchase.TotalCost), purchase.Status,
            purchase.Status == PurchaseStatus.Confirmed ? null : purchase.ExceedsEstimate(remainingEstimate),
            purchase.CostOverrunApprovedByUserId, purchase.CostOverrunApprovedAt, purchase.ConfirmedByUserId, purchase.ConfirmedAt,
            purchase.ReceiptFileIds, purchase.InfoRequestReason,
            purchase.Items
                .Select(item => new PurchaseItemDto(item.PurchaseItemId, item.PurchaseRequestItemId, item.MaterialId, materials[item.MaterialId].Code,
                    materials[item.MaterialId].Name, materials[item.MaterialId].Unit, item.PurchasedQuantity, MoneyDto.InVnd(item.UnitPrice),
                    MoneyDto.InVnd(item.TotalPrice), item.ReceivedUsableQuantity, item.ReceivedUnusableQuantity))
                .ToList(),
            purchase.Version);
}

public sealed record RecordPurchaseCommand(
    Guid PurchaseRequestId,
    string? SupplierName,
    DateOnly? PurchaseDate,
    IReadOnlyList<PurchaseLineInput> Items,
    IReadOnlyList<Guid> ReceiptFileIds);

public sealed record AttachPurchaseReceiptsCommand(Guid PurchaseId, IReadOnlyList<Guid> FileIds);

public sealed record PurchaseDecisionCommand(Guid PurchaseId, long? Version, string? Note);

public sealed record ConfirmDeliveryCommand(Guid PurchaseId, long? Version, IReadOnlyList<DeliveryLineInput> Items);

public interface IPurchaseStore
{
    /// <summary>Tracked, with items.</summary>
    Task<Purchase?> GetAsync(Guid purchaseId, CancellationToken cancellationToken = default);

    /// <summary>All purchases of a request with items, oldest first.</summary>
    Task<IReadOnlyList<Purchase>> ListByRequestAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default);

    void Add(Purchase purchase);

    /// <summary>Saves the unit of work (purchase, request and ledger rows share the context).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>task.purchase-task.assigned</c> (F-RES-01): when the projected PURCHASE task of an APPROVED request has an accepted
/// buyer, the request becomes BUYER_ASSIGNED (BR-30). Runs inside the projection update, same unit of work.
/// </summary>
public sealed class PurchaseBuyerSync(IPurchaseRequestStore requests)
{
    public async Task ApplyAsync(TaskProjection task, CancellationToken cancellationToken)
    {
        if (task.Type != ProjectedTaskType.Purchase || task.PurchaseRequestId is not { } requestId || task.AssigneeUserIds.Length == 0 ||
            task.Status == ProjectedTaskStatus.Cancelled)
        {
            return;
        }

        if (await requests.GetAsync(requestId, cancellationToken) is { } request)
        {
            request.MarkBuyerAssigned(task.AssigneeUserIds[0], DateTimeOffset.UtcNow);
        }
    }
}

/// <summary>Budget left for a request: estimated total minus the cost of purchases already confirmed (BR-32). Null = no estimate.</summary>
internal static class PurchaseBudget
{
    public static decimal? Remaining(PurchaseRequest request, IEnumerable<Purchase> purchases, Guid excludePurchaseId) =>
        request.HasEstimate
            ? request.EstimatedTotal - purchases.Where(p => p.PurchaseId != excludePurchaseId && p.Status == PurchaseStatus.Confirmed).Sum(p => p.TotalCost)
            : null;
}

/// <summary>Loads a purchase with its request and work area for the caller.</summary>
public sealed class PurchaseContext(IPurchaseStore purchases, PurchaseRequestLoader requests)
{
    public async Task<(Purchase Purchase, PurchaseRequest Request, WorkAreaScope Scope)> LoadAsync(Guid purchaseId, CancellationToken cancellationToken)
    {
        var purchase = await purchases.GetAsync(purchaseId, cancellationToken) ?? throw NotFound();
        try
        {
            var (request, scope) = await requests.ForReadAsync(purchase.PurchaseRequestId, cancellationToken);
            return (purchase, request, scope);
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.NotFound)
        {
            throw NotFound();
        }
    }

    public async Task<PurchaseDto> ToDtoAsync(Purchase purchase, PurchaseRequest request, CancellationToken cancellationToken)
    {
        var all = await purchases.ListByRequestAsync(request.PurchaseRequestId, cancellationToken);
        return PurchaseDto.From(purchase, await requests.MaterialsAsync([request], cancellationToken),
            PurchaseBudget.Remaining(request, all, purchase.PurchaseId));
    }

    public static DomainRuleException NotFound() => DomainRuleException.NotFound("PURCHASE_NOT_FOUND", "The purchase does not exist.");
}

/// <summary>
/// F-RES-09 <c>recordPurchase</c> (VOLUNTEER holding the ACCEPTED assignment of a PURCHASE task of the request — BR-30):
/// lines of the request, quantities and prices; request → PURCHASED; stock does <b>not</b> change (BR-26).
/// </summary>
public sealed class RecordPurchaseCommandHandler(
    PurchaseRequestLoader requests,
    ITaskProjectionStore tasks,
    IPurchaseStore store,
    PurchaseContext context,
    ICurrentUser currentUser)
{
    public async Task<PurchaseDto> HandleAsync(RecordPurchaseCommand command, CancellationToken cancellationToken = default)
    {
        var (request, scope) = await requests.ForReadAsync(command.PurchaseRequestId, cancellationToken);
        var task = (await tasks.ListByPurchaseRequestAsync(request.PurchaseRequestId, cancellationToken))
            .FirstOrDefault(candidate => candidate.Status != ProjectedTaskStatus.Cancelled && candidate.AssigneeUserIds.Contains(currentUser.UserId))
            ?? throw DomainRuleException.Forbidden("PURCHASE_NOT_BUYER", "Only the volunteer who accepted the purchase task can record the purchase (BR-30).");
        WorkAreaAccess.EnsureWritable(scope);

        var now = DateTimeOffset.UtcNow;
        var today = ParishClock.Today(now);
        var purchase = Purchase.Record(request.PurchaseRequestId, task.TaskId, currentUser.UserId,
            request.Items.ToDictionary(item => item.PurchaseRequestItemId, item => item.MaterialId),
            command.Items, command.SupplierName, command.PurchaseDate ?? today, command.ReceiptFileIds, today, now);
        request.MarkPurchased(currentUser.UserId, now);

        store.Add(purchase);
        await store.SaveChangesAsync(cancellationToken);
        return await context.ToDtoAsync(purchase, request, cancellationToken);
    }
}

/// <summary>F-RES-09 <c>attachPurchaseReceipt</c> (the buyer): receipt files (≤ 5); NEEDS_INFO goes back to SUBMITTED.</summary>
public sealed class AttachPurchaseReceiptsCommandHandler(PurchaseContext context, IPurchaseStore store, ICurrentUser currentUser)
{
    public async Task<PurchaseDto> HandleAsync(AttachPurchaseReceiptsCommand command, CancellationToken cancellationToken = default)
    {
        var (purchase, request, _) = await context.LoadAsync(command.PurchaseId, cancellationToken);
        if (purchase.BuyerUserId != currentUser.UserId)
        {
            throw PurchaseContext.NotFound();
        }

        purchase.AttachReceipts(command.FileIds);
        await store.SaveChangesAsync(cancellationToken);
        return await context.ToDtoAsync(purchase, request, cancellationToken);
    }
}

/// <summary>
/// F-RES-10 <c>requestPurchaseInfo</c> (MO of the work area) and F-RES-11 <c>approveCostOverrun</c> (LEADER of the work
/// area, BR-32 — only when the cost is above what is left of the approved estimate).
/// </summary>
public sealed class PurchaseReviewCommandHandler(
    PurchaseContext context,
    IPurchaseStore store,
    WorkAreaAccess access,
    ICurrentUser currentUser)
{
    public async Task<PurchaseDto> RequestInfoAsync(PurchaseDecisionCommand command, CancellationToken cancellationToken = default)
    {
        var (purchase, request, scope) = await context.LoadAsync(command.PurchaseId, cancellationToken);
        await access.ForMaterialOfficerWriteAsync(scope.WorkAreaId, cancellationToken);
        purchase.EnsureVersion(command.Version);
        purchase.RequestInfo(command.Note);
        await store.SaveChangesAsync(cancellationToken);
        return await context.ToDtoAsync(purchase, request, cancellationToken);
    }

    public async Task<PurchaseDto> ApproveCostOverrunAsync(PurchaseDecisionCommand command, CancellationToken cancellationToken = default)
    {
        var (purchase, request, scope) = await context.LoadAsync(command.PurchaseId, cancellationToken);
        await access.ForLeaderWriteAsync(scope.WorkAreaId, cancellationToken);
        purchase.EnsureVersion(command.Version);
        var all = await store.ListByRequestAsync(request.PurchaseRequestId, cancellationToken);
        purchase.ApproveCostOverrun(currentUser.UserId, PurchaseBudget.Remaining(request, all, purchase.PurchaseId) ?? decimal.MaxValue,
            DateTimeOffset.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return await context.ToDtoAsync(purchase, request, cancellationToken);
    }
}

/// <summary>
/// F-RES-10 <c>confirmPurchaseDelivery</c> (MO of the work area): usable / unusable per line; only usable is posted as
/// <c>PURCHASE_DELIVERY</c> (reference <c>PURCHASE_ITEM</c>, posted once) and the shortage recalculated — all in one
/// transaction. A short delivery leaves the request PARTIALLY_RECEIVED. Blocked by an unapproved cost overrun (BR-32).
/// <c>purchase.delivery.confirmed</c> lets TaskExecution complete the PURCHASE task (BR-65).
/// </summary>
public sealed class ConfirmPurchaseDeliveryCommandHandler(
    PurchaseContext context,
    IPurchaseStore store,
    WorkAreaAccess access,
    IInventoryStore inventory,
    ShortageTracker shortages,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<PurchaseDto> HandleAsync(ConfirmDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        var (purchase, request, _) = await context.LoadAsync(command.PurchaseId, cancellationToken);
        var scope = await access.ForMaterialOfficerWriteAsync(request.WorkAreaId, cancellationToken);
        purchase.EnsureVersion(command.Version);

        var all = await store.ListByRequestAsync(request.PurchaseRequestId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        purchase.ConfirmDelivery(currentUser.UserId, command.Items, PurchaseBudget.Remaining(request, all, purchase.PurchaseId), now);

        var before = new List<ShortageTracker.Snapshot>();
        foreach (var item in purchase.Items.Where(item => item.ReceivedUsableQuantity > 0))
        {
            before.Add(await shortages.CaptureAsync(scope, item.MaterialId, cancellationToken));
            inventory.Append(MaterialTransaction.Post(
                scope.WorkAreaId, item.MaterialId, TransactionType.PurchaseDelivery, item.ReceivedUsableQuantity!.Value, now, currentUser.UserId,
                null, TransactionReferenceType.PurchaseItem, item.PurchaseItemId));
        }

        var delivered = all.Where(other => other.PurchaseId != purchase.PurchaseId && other.Status == PurchaseStatus.Confirmed)
            .Append(purchase)
            .SelectMany(confirmed => confirmed.Items)
            .GroupBy(item => item.PurchaseRequestItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.ReceivedUsableQuantity ?? 0));
        request.MarkDelivered(delivered, currentUser.UserId, now);

        await store.SaveChangesAsync(cancellationToken);
        foreach (var snapshot in before)
        {
            await shortages.PublishIfChangedAsync(snapshot, cancellationToken);
        }

        await publisher.PublishAsync(
            PurchaseDeliveryConfirmed.EventType,
            PurchaseDeliveryConfirmed.Version,
            new PurchaseDeliveryConfirmed(
                purchase.PurchaseId, request.PurchaseRequestId, purchase.TaskId, scope.WorkAreaId, scope.SeasonId, scope.ParishId,
                purchase.BuyerUserId, currentUser.UserId, purchase.TotalCost, MoneyDto.Vnd,
                request.Status == PurchaseRequestStatus.Completed ? "COMPLETED" : "PARTIALLY_RECEIVED",
                purchase.Items.Select(item => new PurchaseDeliveryLine(item.MaterialId, item.PurchasedQuantity, item.ReceivedUsableQuantity!.Value,
                    item.ReceivedUnusableQuantity!.Value)).ToList(),
                now),
            "purchase",
            purchase.PurchaseId,
            cancellationToken);
        return await context.ToDtoAsync(purchase, request, cancellationToken);
    }
}
