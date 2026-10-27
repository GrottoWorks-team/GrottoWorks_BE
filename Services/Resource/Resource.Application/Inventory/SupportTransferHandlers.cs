using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.Application.Inventory;

public sealed record SupportTransferDto(
    Guid SupportResponseMaterialId,
    Guid SourceWorkAreaId,
    Guid TargetWorkAreaId,
    Guid MaterialId,
    decimal Quantity,
    SupportTransferStatus Status,
    DateTimeOffset TransferredAt,
    decimal? ReceivedUsableQuantity,
    decimal? ReceivedUnusableQuantity,
    Guid? ReceivedConfirmedBy,
    DateTimeOffset? ReceivedConfirmedAt,
    string? Note,
    long Version)
{
    public static SupportTransferDto From(SupportTransfer transfer) =>
        new(transfer.SupportResponseMaterialId, transfer.SourceWorkAreaId, transfer.TargetWorkAreaId, transfer.MaterialId, transfer.Quantity,
            transfer.Status, transfer.TransferredAt, transfer.ReceivedUsableQuantity, transfer.ReceivedUnusableQuantity,
            transfer.ReceivedConfirmedByUserId, transfer.ReceivedConfirmedAt, transfer.Note, transfer.Version);
}

public sealed record StartSupportTransferCommand(
    Guid SupportResponseMaterialId,
    Guid SourceWorkAreaId,
    Guid TargetWorkAreaId,
    Guid MaterialId,
    decimal Quantity,
    Guid ApprovedByUserId);

public sealed record ConfirmTransferReceiptCommand(Guid SupportResponseMaterialId, long? Version, decimal UsableQuantity, decimal UnusableQuantity, string? Note);

public interface ISupportTransferStore
{
    /// <summary>Tracked.</summary>
    Task<SupportTransfer?> GetAsync(Guid supportResponseMaterialId, CancellationToken cancellationToken = default);

    void Add(SupportTransfer transfer);

    /// <summary>Saves the unit of work, including ledger rows appended to the same context.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// F-RES-20 <c>POST /internal/support-transfers</c> — called synchronously by TaskExecution when the leader approves a
/// support material (F-TSK-15b, R-08). Under the source work area's stock lock: the quantity must fit its unallocated
/// stock (BR-63, else 409 <c>MATERIAL_INSUFFICIENT_AVAILABLE</c>), TRANSFER_OUT is posted. Idempotent by the support
/// response material id: a replay with the same data returns the existing transfer (<c>Created = false</c>), different
/// data is a 409.
/// </summary>
public sealed class StartSupportTransferCommandHandler(
    IWorkAreaScopeStore scopes,
    IMaterialCatalogStore catalog,
    InventoryReader reader,
    IInventoryStore inventory,
    ISupportTransferStore store,
    IWorkAreaStockLock stockLock,
    ShortageTracker shortages)
{
    public async Task<(SupportTransferDto Transfer, bool Created)> HandleAsync(StartSupportTransferCommand command, CancellationToken cancellationToken = default)
    {
        if (await store.GetAsync(command.SupportResponseMaterialId, cancellationToken) is { } existing)
        {
            return Replay(existing, command);
        }

        var source = await scopes.GetAsync(command.SourceWorkAreaId, cancellationToken)
            ?? throw DomainRuleException.Validation("WORK_AREA_NOT_PROJECTED", "The source work area is unknown to Resource.");
        var target = await scopes.GetAsync(command.TargetWorkAreaId, cancellationToken)
            ?? throw DomainRuleException.Validation("WORK_AREA_NOT_PROJECTED", "The target work area is unknown to Resource.");
        if (source.SeasonId != target.SeasonId)
        {
            throw DomainRuleException.Validation("SUPPORT_TRANSFER_OTHER_SEASON", "Source and target work areas must belong to the same season.");
        }

        WorkAreaAccess.EnsureWritable(source);
        var material = await catalog.GetMaterialAsync(command.MaterialId, cancellationToken)
            ?? throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");

        var now = DateTimeOffset.UtcNow;
        var transfer = SupportTransfer.Start(command.SupportResponseMaterialId, source.WorkAreaId, target.WorkAreaId, material.MaterialId,
            command.Quantity, command.ApprovedByUserId, now);

        var before = await shortages.CaptureAsync(source, material.MaterialId, cancellationToken);
        await using (var lease = await stockLock.AcquireAsync(source.WorkAreaId, cancellationToken))
        {
            // A concurrent replay may have won the lock first.
            if (await store.GetAsync(command.SupportResponseMaterialId, cancellationToken) is { } raced)
            {
                return Replay(raced, command);
            }

            await StockGuard.EnsureUnallocatedAsync(reader, source, new Dictionary<Guid, Domain.Catalog.Material> { [material.MaterialId] = material },
                [(material.MaterialId, transfer.Quantity)], cancellationToken);
            store.Add(transfer);
            inventory.Append(MaterialTransaction.Post(
                source.WorkAreaId, material.MaterialId, TransactionType.TransferOut, transfer.Quantity, now, command.ApprovedByUserId, null,
                TransactionReferenceType.SupportResponseMaterial, transfer.SupportResponseMaterialId));
            await store.SaveChangesAsync(cancellationToken);
            await lease.CommitAsync(cancellationToken);
        }

        await shortages.PublishIfChangedAsync(before, cancellationToken);
        return (SupportTransferDto.From(transfer), true);
    }

    private static (SupportTransferDto, bool) Replay(SupportTransfer existing, StartSupportTransferCommand command) =>
        existing.Matches(command.SourceWorkAreaId, command.TargetWorkAreaId, command.MaterialId, command.Quantity)
            ? (SupportTransferDto.From(existing), false)
            : throw new DomainRuleException(
                "SUPPORT_TRANSFER_CONFLICT",
                "This support response material was already transferred with different data.");
}

/// <summary>
/// F-RES-20 <c>confirmSupportMaterialReceipt</c> (MO of the receiving work area, one confirmation — R-23): records usable
/// and unusable quantities; only the usable part is posted as TRANSFER_IN (BR-64); then <c>support.transfer.confirmed</c>
/// for TaskExecution.
/// </summary>
public sealed class ConfirmTransferReceiptCommandHandler(
    WorkAreaAccess access,
    ISupportTransferStore store,
    IInventoryStore inventory,
    ShortageTracker shortages,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<SupportTransferDto> HandleAsync(ConfirmTransferReceiptCommand command, CancellationToken cancellationToken = default)
    {
        var transfer = await store.GetAsync(command.SupportResponseMaterialId, cancellationToken)
            ?? throw DomainRuleException.NotFound("SUPPORT_TRANSFER_NOT_FOUND", "No approved transfer exists for this support material.");
        WorkAreaScope target;
        try
        {
            target = await access.ForMaterialOfficerWriteAsync(transfer.TargetWorkAreaId, cancellationToken);
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.NotFound)
        {
            throw DomainRuleException.NotFound("SUPPORT_TRANSFER_NOT_FOUND", "No approved transfer exists for this support material.");
        }

        transfer.EnsureVersion(command.Version);
        var now = DateTimeOffset.UtcNow;
        transfer.ConfirmReceipt(command.UsableQuantity, command.UnusableQuantity, command.Note, currentUser.UserId, now);

        var before = await shortages.CaptureAsync(target, transfer.MaterialId, cancellationToken);
        if (command.UsableQuantity > 0)
        {
            inventory.Append(MaterialTransaction.Post(
                target.WorkAreaId, transfer.MaterialId, TransactionType.TransferIn, command.UsableQuantity, now, currentUser.UserId, null,
                TransactionReferenceType.SupportResponseMaterial, transfer.SupportResponseMaterialId));
        }

        await store.SaveChangesAsync(cancellationToken);
        await shortages.PublishIfChangedAsync(before, cancellationToken);
        await publisher.PublishAsync(
            SupportTransferConfirmed.EventType,
            SupportTransferConfirmed.Version,
            new SupportTransferConfirmed(transfer.SupportResponseMaterialId, transfer.SourceWorkAreaId, transfer.TargetWorkAreaId, target.ParishId,
                transfer.MaterialId, transfer.Quantity, command.UsableQuantity, command.UnusableQuantity, currentUser.UserId, now),
            "support_transfer",
            transfer.SupportResponseMaterialId,
            cancellationToken);
        return SupportTransferDto.From(transfer);
    }
}
