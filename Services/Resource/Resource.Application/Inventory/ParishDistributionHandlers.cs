using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.Application.Inventory;

public sealed record CreateParishDistributionCommand(
    Guid SeasonId,
    Guid TargetWorkAreaId,
    IReadOnlyList<DistributionLineInput> Items,
    string? Note);

public sealed record ParishDistributionFilter(
    int Page,
    int Size,
    Guid? SeasonId,
    Guid? WorkAreaId,
    ParishDistributionStatus? Status,
    Guid? ParishId = null);

public sealed record ConfirmParishDistributionCommand(
    Guid DistributionId,
    long? Version,
    IReadOnlyList<DistributionReceiptLineInput> Items,
    string? Note);

public sealed record CancelParishDistributionCommand(Guid DistributionId, long? Version, string? Reason);

/// <summary>
/// Issues a distribution (manual or the automatic hand-down of a purchase): validates the parish balance under the
/// parish stock lock, posts <c>DISTRIBUTION_OUT</c> per line and saves the aggregate. The caller may already own a
/// transaction (purchase confirmation); the lock then joins it.
/// </summary>
public sealed class ParishDistributionIssuer(
    IParishInventoryStore inventory,
    IParishDistributionStore distributions,
    IParishStockLock stockLock,
    IMaterialCatalogStore catalog,
    IWorkAreaScopeStore scopes,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<ParishDistribution> IssueAsync(
        Guid parishId,
        Guid seasonId,
        Guid targetWorkAreaId,
        IReadOnlyCollection<DistributionLineInput> lines,
        ParishDistributionOrigin origin,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var materials = (await catalog.GetMaterialsAsync(lines.Select(line => line.MaterialId).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);
        if (lines.FirstOrDefault(line => !materials.ContainsKey(line.MaterialId)) is { } unknown)
        {
            throw DomainRuleException.Validation("MATERIAL_UNKNOWN", $"Material {unknown.MaterialId} does not exist.");
        }

        if (materials.Values.FirstOrDefault(material => material.Status != CatalogStatus.Active) is { } inactive)
        {
            throw DomainRuleException.Validation("MATERIAL_INACTIVE", $"Material {inactive.Code} is inactive.");
        }

        var now = DateTimeOffset.UtcNow;
        var distribution = ParishDistribution.Issue(
            parishId, seasonId, targetWorkAreaId, lines, origin, note, currentUser.UserId, now);

        await using (var lease = await stockLock.AcquireAsync(parishId, seasonId, cancellationToken))
        {
            var balances = (await inventory.GetTotalsAsync(parishId, seasonId, null, cancellationToken))
                .ToDictionary(row => row.MaterialId, row => row.Quantity);
            foreach (var item in distribution.Items)
            {
                var available = balances.GetValueOrDefault(item.MaterialId);
                if (item.IssuedQuantity > available)
                {
                    var material = materials[item.MaterialId];
                    throw new DomainRuleException(
                        "PARISH_STOCK_INSUFFICIENT",
                        $"Only {available} {material.Unit} of {material.Code} is available in the parish warehouse; {item.IssuedQuantity} was requested.");
                }

                inventory.Append(ParishMaterialTransaction.Post(
                    parishId, seasonId, item.MaterialId, TransactionType.DistributionOut, item.IssuedQuantity, now,
                    currentUser.UserId, note, TransactionReferenceType.ParishDistributionItem, item.ParishDistributionItemId));
            }

            distributions.Add(distribution);
            await distributions.SaveChangesAsync(cancellationToken);
            await lease.CommitAsync(cancellationToken);
        }

        var target = await scopes.GetAsync(targetWorkAreaId, cancellationToken);
        await publisher.PublishAsync(
            ParishDistributionIssued.EventType,
            ParishDistributionIssued.Version,
            new ParishDistributionIssued(distribution.ParishDistributionId, parishId, seasonId, targetWorkAreaId,
                target?.MaterialOfficerUserId, distribution.Items.Count, currentUser.UserId, now),
            "parish_distribution",
            distribution.ParishDistributionId,
            cancellationToken: cancellationToken);

        foreach (var material in materials.Values)
        {
            var remaining = Math.Max(0, (await inventory.GetTotalsAsync(parishId, seasonId, material.MaterialId, cancellationToken))
                .Sum(row => row.Quantity));
            await publisher.PublishAsync(
                ParishMaterialStockChanged.EventType,
                ParishMaterialStockChanged.Version,
                new ParishMaterialStockChanged(parishId, seasonId, material.MaterialId, remaining, now,
                    material.Code, material.Name, material.Unit),
                "parish_material",
                material.MaterialId,
                cancellationToken: cancellationToken);
        }

        return distribution;
    }
}

/// <summary>F-RES-21 <c>createParishDistribution</c> (PARISH): hands warehouse stock down to one work area.</summary>
public sealed class CreateParishDistributionCommandHandler(
    ParishWarehouseAccess access,
    ParishDistributionIssuer issuer,
    ParishDistributionProjector projector)
{
    public async Task<ParishDistributionDto> HandleAsync(
        CreateParishDistributionCommand command,
        CancellationToken cancellationToken = default)
    {
        var (_, parishId) = await access.RequireWritableSeasonAsync(command.SeasonId, cancellationToken);
        _ = await access.RequireTargetAreaAsync(command.TargetWorkAreaId, parishId, command.SeasonId, cancellationToken);

        var distribution = await issuer.IssueAsync(
            parishId, command.SeasonId, command.TargetWorkAreaId, command.Items, ParishDistributionOrigin.Manual, command.Note,
            cancellationToken);
        return (await projector.ToDtosAsync([distribution], cancellationToken))[0];
    }
}

/// <summary>F-RES-21 <c>listParishDistributions</c>: PARISH sees the parish; LEADER/MO see their work areas only.</summary>
public sealed class ListParishDistributionsQueryHandler(
    ParishWarehouseAccess parishAccess,
    WorkAreaAccess workAreaAccess,
    IWorkAreaScopeStore scopes,
    IParishDistributionStore store,
    ParishDistributionProjector projector,
    ICurrentUser currentUser)
{
    public async Task<PagedResult<ParishDistributionDto>> HandleAsync(
        ParishDistributionFilter filter,
        CancellationToken cancellationToken = default)
    {
        Guid parishId;
        IReadOnlyCollection<Guid>? workAreaIds = null;
        if (currentUser.Role is GrottoWorksRoles.Parish or GrottoWorksRoles.Admin)
        {
            parishId = parishAccess.RequireReadParish(filter.ParishId);
            if (filter.SeasonId is { } seasonId)
            {
                await parishAccess.RequireReadableSeasonAsync(seasonId, parishId, cancellationToken);
            }
        }
        else
        {
            parishId = workAreaAccess.RequireParish();
            var areas = (await scopes.ListByParishAsync(parishId, filter.SeasonId, cancellationToken))
                .Where(area => currentUser.Role == GrottoWorksRoles.MaterialOfficer
                    ? area.MaterialOfficerUserId == currentUser.UserId
                    : area.LeaderUserId == currentUser.UserId)
                .Select(area => area.WorkAreaId)
                .ToList();
            if (filter.WorkAreaId is { } requested)
            {
                areas = areas.Where(id => id == requested).ToList();
            }

            if (areas.Count == 0)
            {
                return new PagedResult<ParishDistributionDto>([], 0);
            }

            workAreaIds = areas;
        }

        if (workAreaIds is null && filter.WorkAreaId is { } workAreaId)
        {
            workAreaIds = [workAreaId];
        }

        var page = await store.SearchAsync(
            new ParishDistributionSearch(parishId, filter.SeasonId, workAreaIds, filter.Status, filter.Page, filter.Size),
            cancellationToken);
        return new PagedResult<ParishDistributionDto>(await projector.ToDtosAsync(page.Items, cancellationToken), page.TotalItems);
    }
}

/// <summary>F-RES-21 <c>getParishDistribution</c>.</summary>
public sealed class GetParishDistributionQueryHandler(
    IParishDistributionStore store,
    IWorkAreaScopeStore scopes,
    ParishDistributionProjector projector,
    ICurrentUser currentUser)
{
    public async Task<ParishDistributionDto> HandleAsync(Guid distributionId, CancellationToken cancellationToken = default)
    {
        var distribution = await store.GetAsync(distributionId, cancellationToken) ?? throw NotFound();
        var target = await scopes.GetAsync(distribution.TargetWorkAreaId, cancellationToken);
        var visible = currentUser.Role switch
        {
            GrottoWorksRoles.Admin => true,
            GrottoWorksRoles.Parish => target?.ParishId == currentUser.ParishId,
            GrottoWorksRoles.MaterialOfficer => target?.MaterialOfficerUserId == currentUser.UserId,
            GrottoWorksRoles.Leader => target?.LeaderUserId == currentUser.UserId,
            _ => false
        };
        if (!visible)
        {
            throw NotFound();
        }

        return (await projector.ToDtosAsync([distribution], cancellationToken))[0];
    }

    internal static DomainRuleException NotFound() =>
        DomainRuleException.NotFound("PARISH_DISTRIBUTION_NOT_FOUND", "The distribution does not exist.");
}

/// <summary>
/// F-RES-21 <c>confirmParishDistributionReceipt</c> (MO of the target work area): usable + unusable ≤ issued; only the
/// usable part becomes TRANSFER_IN in the work-area ledger (BR-64 pattern).
/// </summary>
public sealed class ConfirmParishDistributionCommandHandler(
    WorkAreaAccess access,
    IParishDistributionStore store,
    IInventoryStore inventory,
    ShortageTracker shortages,
    ParishDistributionProjector projector,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public Task<ParishDistributionDto> HandleAsync(
        ConfirmParishDistributionCommand command,
        CancellationToken cancellationToken = default) =>
        store.InTransactionAsync(() => ConfirmInTransactionAsync(command, cancellationToken), cancellationToken);

    private async Task<ParishDistributionDto> ConfirmInTransactionAsync(
        ConfirmParishDistributionCommand command,
        CancellationToken cancellationToken)
    {
        var distribution = await store.GetAsync(command.DistributionId, cancellationToken)
            ?? throw GetParishDistributionQueryHandler.NotFound();

        WorkAreaScope target;
        try
        {
            target = await access.ForMaterialOfficerWriteAsync(distribution.TargetWorkAreaId, cancellationToken);
        }
        catch (DomainRuleException exception) when (exception.Kind is DomainErrorKind.NotFound or DomainErrorKind.Forbidden)
        {
            throw GetParishDistributionQueryHandler.NotFound();
        }

        distribution.EnsureVersion(command.Version);
        var now = DateTimeOffset.UtcNow;
        distribution.ConfirmReceipt(command.Items, command.Note, currentUser.UserId, now);

        var before = new List<ShortageTracker.Snapshot>();
        foreach (var item in distribution.Items.Where(item => item.ReceivedUsableQuantity is { } usable && usable > 0))
        {
            before.Add(await shortages.CaptureAsync(target, item.MaterialId, cancellationToken));
            inventory.Append(MaterialTransaction.Post(
                target.WorkAreaId, item.MaterialId, TransactionType.TransferIn, item.ReceivedUsableQuantity!.Value, now,
                currentUser.UserId, null, TransactionReferenceType.ParishDistributionItem, item.ParishDistributionItemId));
        }

        await store.SaveChangesAsync(cancellationToken);
        foreach (var snapshot in before)
        {
            await shortages.PublishIfChangedAsync(snapshot, cancellationToken);
        }

        await publisher.PublishAsync(
            ParishDistributionReceived.EventType,
            ParishDistributionReceived.Version,
            new ParishDistributionReceived(distribution.ParishDistributionId, distribution.ParishId, distribution.SeasonId,
                distribution.TargetWorkAreaId, currentUser.UserId, distribution.IssuedByUserId,
                distribution.Items.Sum(item => item.ReceivedUsableQuantity ?? 0),
                distribution.Items.Sum(item => item.ReceivedUnusableQuantity ?? 0), now),
            "parish_distribution",
            distribution.ParishDistributionId,
            cancellationToken: cancellationToken);

        return (await projector.ToDtosAsync([distribution], cancellationToken))[0];
    }
}

/// <summary>F-RES-21 <c>cancelParishDistribution</c> (PARISH): withdraws an in-transit shipment and restores stock.</summary>
public sealed class CancelParishDistributionCommandHandler(
    ParishWarehouseAccess access,
    IParishDistributionStore store,
    IParishInventoryStore inventory,
    IMaterialCatalogStore catalog,
    ParishDistributionProjector projector,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public Task<ParishDistributionDto> HandleAsync(
        CancelParishDistributionCommand command,
        CancellationToken cancellationToken = default) =>
        store.InTransactionAsync(() => CancelInTransactionAsync(command, cancellationToken), cancellationToken);

    private async Task<ParishDistributionDto> CancelInTransactionAsync(
        CancelParishDistributionCommand command,
        CancellationToken cancellationToken)
    {
        var distribution = await store.GetAsync(command.DistributionId, cancellationToken)
            ?? throw GetParishDistributionQueryHandler.NotFound();
        var parishId = access.RequireWriteParish();
        if (distribution.ParishId != parishId)
        {
            throw GetParishDistributionQueryHandler.NotFound();
        }

        distribution.EnsureVersion(command.Version);
        var now = DateTimeOffset.UtcNow;
        distribution.Cancel(command.Reason, now);

        var materials = (await catalog.GetMaterialsAsync(distribution.Items.Select(item => item.MaterialId).Distinct().ToList(),
            cancellationToken)).ToDictionary(material => material.MaterialId);
        foreach (var item in distribution.Items)
        {
            inventory.Append(ParishMaterialTransaction.Post(
                distribution.ParishId, distribution.SeasonId, item.MaterialId, TransactionType.DistributionReturn,
                item.IssuedQuantity, now, currentUser.UserId, distribution.CancelReason,
                TransactionReferenceType.ParishDistributionItem, item.ParishDistributionItemId));
        }

        await store.SaveChangesAsync(cancellationToken);
        foreach (var material in materials.Values)
        {
            var available = Math.Max(0, (await inventory.GetTotalsAsync(distribution.ParishId, distribution.SeasonId,
                material.MaterialId, cancellationToken)).Sum(row => row.Quantity));
            await publisher.PublishAsync(
                ParishMaterialStockChanged.EventType,
                ParishMaterialStockChanged.Version,
                new ParishMaterialStockChanged(distribution.ParishId, distribution.SeasonId, material.MaterialId, available, now,
                    material.Code, material.Name, material.Unit),
                "parish_material",
                material.MaterialId,
                cancellationToken: cancellationToken);
        }

        return (await projector.ToDtosAsync([distribution], cancellationToken))[0];
    }
}

/// <summary>Builds <see cref="ParishDistributionDto"/>s with work-area and material labels in two batched reads.</summary>
public sealed class ParishDistributionProjector(
    IWorkAreaScopeStore scopes,
    IMaterialCatalogStore catalog)
{
    public async Task<IReadOnlyList<ParishDistributionDto>> ToDtosAsync(
        IReadOnlyCollection<ParishDistribution> distributions,
        CancellationToken cancellationToken)
    {
        if (distributions.Count == 0)
        {
            return [];
        }

        var materials = (await catalog.GetMaterialsAsync(
                distributions.SelectMany(distribution => distribution.Items).Select(item => item.MaterialId).Distinct().ToList(),
                cancellationToken))
            .ToDictionary(material => material.MaterialId);
        var names = (await scopes.ListByIdsAsync(distributions.Select(distribution => distribution.TargetWorkAreaId).Distinct().ToList(),
                cancellationToken))
            .ToDictionary(scope => scope.WorkAreaId, scope => scope.Name);

        return distributions
            .Select(distribution => From(distribution, names.GetValueOrDefault(distribution.TargetWorkAreaId, string.Empty), materials))
            .ToList();
    }

    internal static ParishDistributionDto From(
        ParishDistribution distribution,
        string targetName,
        IReadOnlyDictionary<Guid, Material> materials) =>
        new(distribution.ParishDistributionId, distribution.ParishId, distribution.SeasonId, distribution.TargetWorkAreaId,
            targetName, distribution.Status, distribution.Origin, distribution.IssuedByUserId, distribution.IssuedAt,
            distribution.ReceivedConfirmedByUserId, distribution.ReceivedConfirmedAt, distribution.Note, distribution.CancelReason,
            distribution.Items
                .Select(item =>
                {
                    var material = materials.GetValueOrDefault(item.MaterialId);
                    return new ParishDistributionItemDto(item.ParishDistributionItemId, item.MaterialId,
                        material?.Code ?? string.Empty, material?.Name ?? item.MaterialId.ToString(), material?.Unit ?? string.Empty,
                        item.IssuedQuantity, item.ReceivedUsableQuantity, item.ReceivedUnusableQuantity);
                })
                .OrderBy(item => item.MaterialName, StringComparer.CurrentCulture)
                .ToList(),
            distribution.Version);
}
