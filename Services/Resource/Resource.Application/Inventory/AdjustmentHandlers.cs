using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Domain;
using Resource.Domain.Inventory;

namespace Resource.Application.Inventory;

/// <summary>Why stock leaves the usable inventory (ERD transaction types <c>ADJUSTMENT_*</c>).</summary>
public enum AdjustmentKind
{
    Damaged,
    Lost,
    Disposed
}

public sealed record CreateInventoryAdjustmentCommand(
    Guid WorkAreaId,
    Guid MaterialId,
    AdjustmentKind Kind,
    decimal Quantity,
    DateTimeOffset? TransactionDate,
    string? Reason);

/// <summary>
/// F-RES-16 <c>createInventoryAdjustment</c> (MO of the work area): takes damaged, lost or disposed units out of usable
/// stock — never back in (BR-43). Only unallocated stock is in the officer's hands, so the quantity must fit it (under
/// the work-area stock lock). The ledger row is its own source record (reference <c>INVENTORY_ADJUSTMENT</c>).
/// </summary>
public sealed class CreateInventoryAdjustmentCommandHandler(
    WorkAreaAccess access,
    IMaterialCatalogStore catalog,
    InventoryReader reader,
    IInventoryStore inventory,
    IWorkAreaStockLock stockLock,
    ShortageTracker shortages,
    ICurrentUser currentUser)
{
    public const int MaxReasonLength = 500;

    public async Task<InventoryTransactionDto> HandleAsync(CreateInventoryAdjustmentCommand command, CancellationToken cancellationToken = default)
    {
        var scope = await access.ForMaterialOfficerWriteAsync(command.WorkAreaId, cancellationToken);
        var material = await catalog.GetMaterialAsync(command.MaterialId, cancellationToken)
            ?? throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        if (reason is null || reason.Length > MaxReasonLength)
        {
            throw DomainRuleException.Validation("ADJUSTMENT_REASON_REQUIRED", $"A reason of at most {MaxReasonLength} characters is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var date = command.TransactionDate ?? now;
        if (date > now + TimeSpan.FromMinutes(5))
        {
            throw DomainRuleException.Validation("TRANSACTION_DATE_IN_FUTURE", "An adjustment cannot be dated in the future.");
        }

        var adjustmentId = Guid.NewGuid();
        var transaction = MaterialTransaction.Post(
            scope.WorkAreaId, material.MaterialId, ToType(command.Kind), command.Quantity, date, currentUser.UserId, reason,
            TransactionReferenceType.InventoryAdjustment, adjustmentId);

        var before = await shortages.CaptureAsync(scope, material.MaterialId, cancellationToken);
        await using (var lease = await stockLock.AcquireAsync(scope.WorkAreaId, cancellationToken))
        {
            await StockGuard.EnsureUnallocatedAsync(reader, scope, new Dictionary<Guid, Domain.Catalog.Material> { [material.MaterialId] = material },
                [(material.MaterialId, command.Quantity)], cancellationToken);
            inventory.Append(transaction);
            await inventory.SaveChangesAsync(cancellationToken);
            await lease.CommitAsync(cancellationToken);
        }

        await shortages.PublishIfChangedAsync(before, cancellationToken);
        return InventoryTransactionDto.From(transaction);
    }

    private static TransactionType ToType(AdjustmentKind kind) => kind switch
    {
        AdjustmentKind.Damaged => TransactionType.AdjustmentDamaged,
        AdjustmentKind.Lost => TransactionType.AdjustmentLost,
        _ => TransactionType.AdjustmentDisposed
    };
}
