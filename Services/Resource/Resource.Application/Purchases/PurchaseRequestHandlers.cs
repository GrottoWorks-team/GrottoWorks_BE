using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Purchases;
using Resource.Domain.Scope;

namespace Resource.Application.Purchases;

public sealed record MoneyDto(decimal Amount, string Currency)
{
    public const string Vnd = "VND";

    public static MoneyDto InVnd(decimal amount) => new(amount, Vnd);
}

/// <summary><c>PurchaseRequestItem</c> plus the catalog fields clients need to render it.</summary>
public sealed record PurchaseRequestItemDto(
    Guid Id,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal RequestedQuantity,
    decimal? EstimatedCost,
    string? Note,
    string? ShortageOverrideReason);

/// <summary><c>PurchaseRequest</c> (API Contract v1.3, Resources).</summary>
public sealed record PurchaseRequestDto(
    Guid Id,
    Guid WorkAreaId,
    string WorkAreaName,
    Guid SeasonId,
    Guid RequestedBy,
    string? Reason,
    PurchaseRequestStatus Status,
    IReadOnlyList<PurchaseRequestItemDto> Items,
    MoneyDto EstimatedTotal,
    Guid? ApprovedBy,
    string? DecisionNote,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    long Version)
{
    public static PurchaseRequestDto From(PurchaseRequest request, WorkAreaScope scope, IReadOnlyDictionary<Guid, Material> materials) =>
        new(request.PurchaseRequestId, request.WorkAreaId, scope.Name, scope.SeasonId, request.RequestedByUserId, request.RequestReason,
            request.Status,
            request.Items
                .Select(item =>
                {
                    var material = materials[item.MaterialId];
                    return new PurchaseRequestItemDto(item.PurchaseRequestItemId, item.MaterialId, material.Code, material.Name,
                        material.Unit, item.RequestedQuantity, item.EstimatedCost, item.Note, item.ShortageOverrideReason);
                })
                .OrderBy(item => item.MaterialName, StringComparer.CurrentCulture)
                .ToList(),
            MoneyDto.InVnd(request.EstimatedTotal), request.ApprovedByUserId, request.DecisionNote, request.ApprovedAt,
            request.RejectedAt, request.CreatedAt, request.UpdatedAt, request.Version);
}

/// <summary><c>StateChange</c>: one status transition of a purchase request (from = null on creation).</summary>
public sealed record StateChangeDto(
    PurchaseRequestStatus? FromStatus,
    PurchaseRequestStatus ToStatus,
    Guid ChangedBy,
    DateTimeOffset ChangedAt,
    string? Note);

public sealed record PurchaseRequestFilter(
    int Page,
    int Size,
    Guid? SeasonId,
    Guid? WorkAreaId,
    PurchaseRequestStatus? Status,
    bool NewestFirst);

public sealed record CreatePurchaseRequestCommand(Guid WorkAreaId, string? Reason, IReadOnlyList<PurchaseRequestItemInput> Items);

public sealed record UpdatePurchaseRequestCommand(
    Guid PurchaseRequestId,
    long? Version,
    string? Reason,
    IReadOnlyList<PurchaseRequestItemInput>? Items);

public sealed record PurchaseRequestCommand(Guid PurchaseRequestId, long? Version, string? Note = null);

public static class PurchaseRequestRules
{
    /// <summary>
    /// Requests whose quantity is asked for but not delivered yet. PARTIALLY_RECEIVED joins once F-RES-10 tracks the
    /// delivered part per line (its delivered part is already in the ledger).
    /// </summary>
    public static readonly IReadOnlySet<PurchaseRequestStatus> OpenStatuses = new HashSet<PurchaseRequestStatus>
    {
        PurchaseRequestStatus.PendingApproval,
        PurchaseRequestStatus.Approved,
        PurchaseRequestStatus.BuyerAssigned,
        PurchaseRequestStatus.Purchased
    };
}

/// <summary>Loads a request and the work area it belongs to, applying parish tenancy (404 outside the parish).</summary>
public sealed class PurchaseRequestLoader(IPurchaseRequestStore store, WorkAreaAccess access, IMaterialCatalogStore catalog)
{
    public async Task<(PurchaseRequest Request, WorkAreaScope Scope)> ForReadAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await store.GetAsync(requestId, cancellationToken) ?? throw NotFound();
        try
        {
            return (request, await access.ForReadAsync(request.WorkAreaId, cancellationToken));
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.NotFound)
        {
            // Another parish must not learn that the id exists.
            throw NotFound();
        }
    }

    public async Task<PurchaseRequestDto> ToDtoAsync(PurchaseRequest request, WorkAreaScope scope, CancellationToken cancellationToken)
    {
        var materials = await MaterialsAsync([request], cancellationToken);
        return PurchaseRequestDto.From(request, scope, materials);
    }

    public async Task<IReadOnlyDictionary<Guid, Material>> MaterialsAsync(
        IReadOnlyCollection<PurchaseRequest> requests,
        CancellationToken cancellationToken)
    {
        var ids = requests.SelectMany(request => request.Items).Select(item => item.MaterialId).Distinct().ToList();
        return (await catalog.GetMaterialsAsync(ids, cancellationToken)).ToDictionary(material => material.MaterialId);
    }

    public static DomainRuleException NotFound() =>
        DomainRuleException.NotFound("PURCHASE_REQUEST_NOT_FOUND", "The purchase request does not exist.");
}

/// <summary>
/// BA 8.6 "không tạo request khi shortage bằng 0, trừ khi có override và lý do": each line may ask for at most the
/// work area's shortage minus what other open requests already ask for; more needs a per-line override reason.
/// </summary>
public sealed class PurchaseShortageGuard(InventoryReader reader, IPurchaseRequestStore store)
{
    public async Task EnsureCoveredAsync(WorkAreaScope scope, PurchaseRequest request, CancellationToken cancellationToken)
    {
        var uncheckedItems = request.Items.Where(item => item.ShortageOverrideReason is null).ToList();
        if (uncheckedItems.Count == 0)
        {
            return;
        }

        var shortages = (await reader.ReadAsync([scope], null, cancellationToken))
            .ToDictionary(row => row.MaterialId, row => row.Shortage);
        var open = (await store.GetOpenRequestedQuantitiesAsync(scope.WorkAreaId, request.PurchaseRequestId, cancellationToken))
            .ToDictionary(row => row.MaterialId, row => row.Quantity);

        foreach (var item in uncheckedItems)
        {
            var uncovered = Math.Max(0, shortages.GetValueOrDefault(item.MaterialId) - open.GetValueOrDefault(item.MaterialId));
            if (item.RequestedQuantity > uncovered)
            {
                throw new DomainRuleException(
                    "PURCHASE_EXCEEDS_SHORTAGE",
                    $"Material {item.MaterialId} asks for {item.RequestedQuantity} but only {uncovered} is short and not yet requested. " +
                    "Lower the quantity or give a shortage override reason for the line.");
            }
        }
    }
}

/// <summary>Validates the catalog side of request lines: every material exists and is ACTIVE.</summary>
internal static class PurchaseItemCatalog
{
    public static async Task EnsureActiveAsync(
        IMaterialCatalogStore catalog,
        IReadOnlyCollection<PurchaseRequestItemInput> items,
        CancellationToken cancellationToken)
    {
        var ids = items.Select(item => item.MaterialId).Distinct().ToList();
        var materials = (await catalog.GetMaterialsAsync(ids, cancellationToken)).ToDictionary(material => material.MaterialId);
        foreach (var id in ids)
        {
            if (!materials.TryGetValue(id, out var material))
            {
                throw DomainRuleException.Validation("MATERIAL_UNKNOWN", $"Material {id} does not exist.");
            }

            if (material.Status != CatalogStatus.Active)
            {
                throw DomainRuleException.Validation("MATERIAL_INACTIVE", $"Material {material.Code} is inactive.");
            }
        }
    }
}

/// <summary>F-RES-06 <c>listPurchaseRequests</c> (PARISH, LEADER, MO): requests of the caller's parish, newest first.</summary>
public sealed class ListPurchaseRequestsQueryHandler(
    WorkAreaAccess access,
    IWorkAreaScopeStore scopes,
    IPurchaseRequestStore store,
    PurchaseRequestLoader loader)
{
    public async Task<PagedResult<PurchaseRequestDto>> HandleAsync(PurchaseRequestFilter filter, CancellationToken cancellationToken = default)
    {
        var parishId = access.RequireParish();
        var areas = (await scopes.ListByParishAsync(parishId, filter.SeasonId, cancellationToken))
            .Where(area => filter.WorkAreaId is null || area.WorkAreaId == filter.WorkAreaId)
            .ToDictionary(area => area.WorkAreaId);
        if (areas.Count == 0)
        {
            return new PagedResult<PurchaseRequestDto>([], 0);
        }

        var page = await store.SearchAsync(
            new PurchaseRequestSearch(areas.Keys.ToList(), filter.Status, filter.Page, filter.Size, filter.NewestFirst),
            cancellationToken);
        var materials = await loader.MaterialsAsync(page.Items, cancellationToken);

        return new PagedResult<PurchaseRequestDto>(
            page.Items.Select(request => PurchaseRequestDto.From(request, areas[request.WorkAreaId], materials)).ToList(),
            page.TotalItems);
    }
}

/// <summary>
/// F-RES-06 <c>getPurchaseRequest</c> (PARISH, LEADER, MO, VOLUNTEER of the parish). A volunteer may only see the
/// request of a PURCHASE task they accepted — the buyer projection arrives with <c>task.purchase-task.assigned</c>
/// (F-RES-01), so until then volunteers get 404.
/// </summary>
public sealed class GetPurchaseRequestQueryHandler(PurchaseRequestLoader loader, ICurrentUser currentUser)
{
    public async Task<PurchaseRequestDto> HandleAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var (request, scope) = await loader.ForReadAsync(requestId, cancellationToken);
        if (currentUser.Role == GrottoWorksRoles.Volunteer)
        {
            throw PurchaseRequestLoader.NotFound();
        }

        return await loader.ToDtoAsync(request, scope, cancellationToken);
    }
}

/// <summary>F-RES-06 <c>getPurchaseRequestHistory</c> (PARISH, LEADER, MO): status and approval history, oldest first.</summary>
public sealed class GetPurchaseRequestHistoryQueryHandler(PurchaseRequestLoader loader, IPurchaseRequestStore store)
{
    public async Task<IReadOnlyList<StateChangeDto>> HandleAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        await loader.ForReadAsync(requestId, cancellationToken);
        return (await store.ListStateChangesAsync(requestId, cancellationToken))
            .Select(change => new StateChangeDto(change.FromStatus, change.ToStatus, change.ChangedByUserId, change.ChangedAt, change.Note))
            .ToList();
    }
}

/// <summary>F-RES-06 <c>createPurchaseRequest</c> (MO of the work area): a DRAFT with zero or more lines.</summary>
public sealed class CreatePurchaseRequestCommandHandler(
    WorkAreaAccess access,
    IPurchaseRequestStore store,
    IMaterialCatalogStore catalog,
    PurchaseShortageGuard guard,
    PurchaseRequestLoader loader,
    ICurrentUser currentUser)
{
    public async Task<PurchaseRequestDto> HandleAsync(CreatePurchaseRequestCommand command, CancellationToken cancellationToken = default)
    {
        var scope = await access.ForMaterialOfficerWriteAsync(command.WorkAreaId, cancellationToken);

        var request = PurchaseRequest.Create(command.WorkAreaId, currentUser.UserId, command.Reason, command.Items, DateTimeOffset.UtcNow);
        await PurchaseItemCatalog.EnsureActiveAsync(catalog, command.Items, cancellationToken);
        await guard.EnsureCoveredAsync(scope, request, cancellationToken);

        store.Add(request);
        await store.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(request, scope, cancellationToken);
    }
}

/// <summary>F-RES-06 <c>updatePurchaseRequest</c> (MO of the work area): reason and/or full line replacement, DRAFT only.</summary>
public sealed class UpdatePurchaseRequestCommandHandler(
    WorkAreaAccess access,
    IPurchaseRequestStore store,
    IMaterialCatalogStore catalog,
    PurchaseShortageGuard guard,
    PurchaseRequestLoader loader)
{
    public async Task<PurchaseRequestDto> HandleAsync(UpdatePurchaseRequestCommand command, CancellationToken cancellationToken = default)
    {
        var (request, _) = await loader.ForReadAsync(command.PurchaseRequestId, cancellationToken);
        var scope = await access.ForMaterialOfficerWriteAsync(request.WorkAreaId, cancellationToken);
        request.EnsureVersion(command.Version);

        request.UpdateDraft(command.Reason, command.Items, DateTimeOffset.UtcNow);
        if (command.Items is not null)
        {
            await PurchaseItemCatalog.EnsureActiveAsync(catalog, command.Items, cancellationToken);
            await guard.EnsureCoveredAsync(scope, request, cancellationToken);
        }

        await store.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(request, scope, cancellationToken);
    }
}

/// <summary>
/// F-RES-06 <c>submitPurchaseRequest</c> (MO of the work area): DRAFT → PENDING_APPROVAL (BR-27), shortage re-checked
/// against current stock, then <c>purchase.request.submitted</c> to the work-area leader.
/// </summary>
public sealed class SubmitPurchaseRequestCommandHandler(
    WorkAreaAccess access,
    IPurchaseRequestStore store,
    PurchaseShortageGuard guard,
    PurchaseRequestLoader loader,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<PurchaseRequestDto> HandleAsync(PurchaseRequestCommand command, CancellationToken cancellationToken = default)
    {
        var (request, _) = await loader.ForReadAsync(command.PurchaseRequestId, cancellationToken);
        var scope = await access.ForMaterialOfficerWriteAsync(request.WorkAreaId, cancellationToken);
        request.EnsureVersion(command.Version);

        var now = DateTimeOffset.UtcNow;
        request.Submit(currentUser.UserId, now);
        await guard.EnsureCoveredAsync(scope, request, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await publisher.PublishAsync(
            PurchaseRequestSubmitted.EventType,
            PurchaseRequestSubmitted.Version,
            new PurchaseRequestSubmitted(
                request.PurchaseRequestId, scope.WorkAreaId, scope.SeasonId, scope.ParishId, request.RequestedByUserId,
                scope.LeaderUserId, request.RequestReason!, request.EstimatedTotal, MoneyDto.Vnd, Lines(request), now),
            "purchase_request",
            request.PurchaseRequestId,
            cancellationToken);
        return await loader.ToDtoAsync(request, scope, cancellationToken);
    }

    internal static IReadOnlyList<PurchaseRequestLine> Lines(PurchaseRequest request) =>
        request.Items
            .Select(item => new PurchaseRequestLine(item.PurchaseRequestItemId, item.MaterialId, item.RequestedQuantity, item.EstimatedCost))
            .ToList();
}

/// <summary>
/// F-RES-07 <c>approvePurchaseRequest</c> / <c>rejectPurchaseRequest</c> (LEADER of the request's work area — D-01,
/// BR-28/29): PENDING_APPROVAL → APPROVED or REJECTED (reason required, BR-31), then <c>purchase.request.decided</c>
/// so Task lets the leader create the PURCHASE task.
/// </summary>
public sealed class DecidePurchaseRequestCommandHandler(
    WorkAreaAccess access,
    IPurchaseRequestStore store,
    PurchaseRequestLoader loader,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public Task<PurchaseRequestDto> ApproveAsync(PurchaseRequestCommand command, CancellationToken cancellationToken = default) =>
        DecideAsync(command, approve: true, cancellationToken);

    public Task<PurchaseRequestDto> RejectAsync(PurchaseRequestCommand command, CancellationToken cancellationToken = default) =>
        DecideAsync(command, approve: false, cancellationToken);

    private async Task<PurchaseRequestDto> DecideAsync(PurchaseRequestCommand command, bool approve, CancellationToken cancellationToken)
    {
        var (request, _) = await loader.ForReadAsync(command.PurchaseRequestId, cancellationToken);
        var scope = await access.ForLeaderWriteAsync(request.WorkAreaId, cancellationToken);
        request.EnsureVersion(command.Version);

        var now = DateTimeOffset.UtcNow;
        if (approve)
        {
            request.Approve(currentUser.UserId, command.Note, now);
        }
        else
        {
            request.Reject(currentUser.UserId, command.Note, now);
        }

        await store.SaveChangesAsync(cancellationToken);

        await publisher.PublishAsync(
            PurchaseRequestDecided.EventType,
            PurchaseRequestDecided.Version,
            new PurchaseRequestDecided(
                request.PurchaseRequestId, scope.WorkAreaId, scope.SeasonId, scope.ParishId, request.RequestedByUserId,
                approve ? PurchaseRequestDecided.Approved : PurchaseRequestDecided.Rejected, currentUser.UserId,
                request.DecisionNote, request.EstimatedTotal, MoneyDto.Vnd, SubmitPurchaseRequestCommandHandler.Lines(request), now),
            "purchase_request",
            request.PurchaseRequestId,
            cancellationToken);
        return await loader.ToDtoAsync(request, scope, cancellationToken);
    }
}
