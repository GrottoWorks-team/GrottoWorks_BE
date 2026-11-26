using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Contributions;
using Resource.Application.Files;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Application.Users;
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
    Guid? RecordedBy,
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
        new(purchase.PurchaseId, purchase.PurchaseRequestId, purchase.TaskId, purchase.BuyerUserId, purchase.RecordedByUserId,
            purchase.SupplierName, purchase.PurchaseDate,
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
/// F-RES-09 <c>recordPurchase</c> (MO of the work area records the trip; buyer = the parish user assigned to the
/// PURCHASE task of the request, else the parish user who approved it — BR-30):
/// lines of the request, quantities and prices; request → PURCHASED; stock does <b>not</b> change (BR-26).
/// </summary>
public sealed class RecordPurchaseCommandHandler(
    PurchaseRequestLoader requests,
    ITaskProjectionStore tasks,
    IPurchaseStore store,
    PurchaseContext context,
    WorkAreaAccess access,
    IIdentityUserLookup users,
    ICurrentUser currentUser,
    IFileVerifier files)
{
    public async Task<PurchaseDto> HandleAsync(RecordPurchaseCommand command, CancellationToken cancellationToken = default)
    {
        var (request, scope) = await requests.ForReadAsync(command.PurchaseRequestId, cancellationToken);
        await access.ForMaterialOfficerWriteAsync(request.WorkAreaId, cancellationToken);

        // BR-30 (changed): the material officer records the trip; the buyer is the parish user assigned to the
        // PURCHASE task of this request, or — when the officer/leader assignment holds a non-parish user — the
        // parish user who approved the request.
        var task = (await tasks.ListByPurchaseRequestAsync(request.PurchaseRequestId, cancellationToken))
            .Where(candidate => candidate.Status != ProjectedTaskStatus.Cancelled)
            .OrderBy(candidate => candidate.TaskId)
            .FirstOrDefault()
            ?? throw new DomainRuleException(
                "PURCHASE_TASK_REQUIRED",
                "Create the PURCHASE task of this request before recording the purchase.");

        var buyer = await ResolveBuyerAsync(task.AssigneeUserIds, request.ApprovedByUserId, scope, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var today = ParishClock.Today(now);
        var purchase = Purchase.Record(request.PurchaseRequestId, task.TaskId, buyer.UserId, currentUser.UserId,
            request.Items.ToDictionary(item => item.PurchaseRequestItemId, item => item.MaterialId),
            command.Items, command.SupplierName, command.PurchaseDate ?? today, command.ReceiptFileIds, today, now);
        request.MarkPurchased(currentUser.UserId, now);

        await using var reservation = await files.ReserveAsync(command.ReceiptFileIds, FilePurposes.PurchaseReceipt, currentUser.UserId, purchase.PurchaseId, cancellationToken);
        store.Add(purchase);
        await store.SaveChangesAsync(cancellationToken);
        return await context.ToDtoAsync(purchase, request, cancellationToken);
    }

    private async Task<Resource.Application.Users.IdentityUser> ResolveBuyerAsync(
        IReadOnlyList<Guid> assigneeUserIds,
        Guid? approvedByUserId,
        WorkAreaScope scope,
        CancellationToken cancellationToken)
    {
        var candidates = new List<Guid>(assigneeUserIds);
        if (approvedByUserId is { } approver)
        {
            candidates.Add(approver);
        }

        candidates = candidates.Where(candidate => candidate != Guid.Empty).Distinct().ToList();
        if (candidates.Count == 0)
        {
            throw new DomainRuleException(
                "PURCHASE_BUYER_NOT_ASSIGNED",
                "Assign a parish user to the PURCHASE task before recording the purchase.");
        }

        foreach (var candidate in candidates)
        {
            Resource.Application.Users.IdentityUser? buyer;
            try
            {
                buyer = await users.GetByIdAsync(candidate, cancellationToken);
            }
            catch (IdentityUserLookupUnavailableException)
            {
                throw new DomainRuleException(
                    "IDENTITY_SERVICE_UNAVAILABLE",
                    "The buyer could not be verified with Identity. Try again later.",
                    DomainErrorKind.ServiceUnavailable);
            }

            if (buyer is not null &&
                string.Equals(buyer.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
                buyer.Role == GrottoWorksRoles.Parish &&
                buyer.ParishId == scope.ParishId)
            {
                return buyer;
            }
        }

        throw DomainRuleException.Validation(
            "PURCHASE_BUYER_NOT_PARISH",
            "The buyer assigned to the PURCHASE task must be an active parish user of the same parish.");
    }
}

/// <summary>F-RES-09 <c>attachPurchaseReceipt</c> (the buyer): receipt files (≤ 5); NEEDS_INFO goes back to SUBMITTED.</summary>
public sealed class AttachPurchaseReceiptsCommandHandler(PurchaseContext context, IPurchaseStore store, ICurrentUser currentUser, IFileVerifier files)
{
    public async Task<PurchaseDto> HandleAsync(AttachPurchaseReceiptsCommand command, CancellationToken cancellationToken = default)
    {
        var (purchase, request, scope) = await context.LoadAsync(command.PurchaseId, cancellationToken);
        var canAttach = purchase.BuyerUserId == currentUser.UserId
            || purchase.RecordedByUserId == currentUser.UserId
            || (currentUser.Role == GrottoWorksRoles.MaterialOfficer && scope.MaterialOfficerUserId == currentUser.UserId);
        if (!canAttach)
        {
            throw PurchaseContext.NotFound();
        }

        purchase.AttachReceipts(command.FileIds);
        await using var reservation = await files.ReserveAsync(command.FileIds, FilePurposes.PurchaseReceipt, currentUser.UserId, purchase.PurchaseId, cancellationToken);
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
    Resource.Application.Scope.ParishWarehouseAccess parishAccess,
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
        var (_, parishId) = await parishAccess.RequireWritableSeasonAsync(scope.SeasonId, cancellationToken);
        if (parishId != scope.ParishId)
        {
            throw PurchaseContext.NotFound();
        }

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
    IParishInventoryStore parishInventory,
    ParishDistributionIssuer distributions,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    // Ledger, request status and purchase.delivery.confirmed commit together: TaskExecution closes the PURCHASE task on it.
    public Task<PurchaseDto> HandleAsync(ConfirmDeliveryCommand command, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(() => ConfirmInTransactionAsync(command, cancellationToken), cancellationToken);

    private async Task<PurchaseDto> ConfirmInTransactionAsync(ConfirmDeliveryCommand command, CancellationToken cancellationToken)
    {
        var (purchase, request, _) = await context.LoadAsync(command.PurchaseId, cancellationToken);
        var scope = await access.ForMaterialOfficerWriteAsync(request.WorkAreaId, cancellationToken);
        purchase.EnsureVersion(command.Version);

        var all = await store.ListByRequestAsync(request.PurchaseRequestId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        purchase.ConfirmDelivery(currentUser.UserId, command.Items, PurchaseBudget.Remaining(request, all, purchase.PurchaseId), now);

        // The purchase belongs to one work area, but the stock lands in the parish warehouse first (F-RES-21).
        foreach (var item in purchase.Items.Where(item => item.ReceivedUsableQuantity > 0))
        {
            parishInventory.Append(ParishMaterialTransaction.Post(
                scope.ParishId, scope.SeasonId, item.MaterialId, TransactionType.PurchaseDelivery, item.ReceivedUsableQuantity!.Value,
                now, currentUser.UserId, null, TransactionReferenceType.PurchaseItem, item.PurchaseItemId));
        }

        var delivered = all.Where(other => other.PurchaseId != purchase.PurchaseId && other.Status == PurchaseStatus.Confirmed)
            .Append(purchase)
            .SelectMany(confirmed => confirmed.Items)
            .GroupBy(item => item.PurchaseRequestItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.ReceivedUsableQuantity ?? 0));
        request.MarkDelivered(delivered, currentUser.UserId, now);

        // Flush the delivery rows before the issuer reads the parish balance (same transaction).
        await store.SaveChangesAsync(cancellationToken);

        var lines = purchase.Items
            .Where(item => item.ReceivedUsableQuantity is { } usable && usable > 0)
            .Select(item => new DistributionLineInput(item.MaterialId, item.ReceivedUsableQuantity!.Value))
            .ToList();
        if (lines.Count > 0)
        {
            await distributions.IssueAsync(
                scope.ParishId, scope.SeasonId, scope.WorkAreaId, lines, ParishDistributionOrigin.Purchase,
                $"Hàng nhập từ đơn mua {purchase.PurchaseId:D}", cancellationToken);
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
