using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.Application.Inventory;

/// <summary><c>InventoryTransaction</c>: one ledger row as returned to clients.</summary>
public sealed record InventoryTransactionDto(
    Guid Id,
    Guid WorkAreaId,
    Guid MaterialId,
    TransactionType TransactionType,
    decimal Quantity,
    DateTimeOffset TransactionDate,
    TransactionReferenceType? ReferenceType,
    Guid? ReferenceId,
    Guid? CreatedByUserId,
    string? Note)
{
    public static InventoryTransactionDto From(MaterialTransaction transaction) =>
        new(transaction.MaterialTransactionId, transaction.WorkAreaId, transaction.MaterialId, transaction.Type, transaction.Quantity,
            transaction.TransactionDate, transaction.ReferenceType, transaction.ReferenceId, transaction.CreatedByUserId, transaction.Note);
}

/// <summary><c>InventoryBalance</c> (API Contract 6, Resources) for one material in one work area.</summary>
public sealed record InventoryBalanceDto(
    Guid WorkAreaId,
    string WorkAreaName,
    Guid SeasonId,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal Required,
    decimal ConfirmedAvailable,
    decimal Allocated,
    decimal Unallocated,
    decimal Shortage)
{
    public static InventoryBalanceDto From(WorkAreaScope scope, Material material, InventoryFigures figures) =>
        new(scope.WorkAreaId, scope.Name, scope.SeasonId, material.MaterialId, material.Code, material.Name, material.Unit,
            figures.Required, figures.ConfirmedAvailable, figures.Allocated, figures.Unallocated, figures.Shortage);
}
