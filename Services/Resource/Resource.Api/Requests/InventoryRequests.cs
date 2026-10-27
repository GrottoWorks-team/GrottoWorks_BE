using System.ComponentModel.DataAnnotations;

namespace Resource.Api.Requests;

/// <summary><c>OpeningStockRequest</c> (F-RES-04): confirmed usable quantity a work area starts with.</summary>
public sealed class OpeningStockRequest
{
    [Required(ErrorMessage = "Work area is required.")]
    public Guid? WorkAreaId { get; init; }

    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Quantity is required.")]
    public decimal? Quantity { get; init; }

    /// <summary>Optional count date; defaults to the server time and cannot be in the future.</summary>
    public DateTimeOffset? TransactionDate { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

public sealed class AllocationLineRequest
{
    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Quantity is required.")]
    public decimal? Quantity { get; init; }
}

/// <summary><c>CreateAllocationRequest</c> (F-RES-15): confirmed stock of a work area, optionally for one of its tasks.</summary>
public sealed class CreateAllocationRequest
{
    [Required(ErrorMessage = "Work area is required.")]
    public Guid? WorkAreaId { get; init; }

    public Guid? TaskId { get; init; }

    [Required(ErrorMessage = "Items are required.")]
    public List<AllocationLineRequest>? Items { get; init; }

    public DateTimeOffset? AllocationDate { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>InventoryAdjustmentRequest</c> (F-RES-16): damaged, lost or disposed units leave usable stock.</summary>
public sealed class InventoryAdjustmentRequest
{
    [Required(ErrorMessage = "Work area is required.")]
    public Guid? WorkAreaId { get; init; }

    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Adjustment type is required.")]
    public Resource.Application.Inventory.AdjustmentKind? Type { get; init; }

    [Required(ErrorMessage = "Quantity is required.")]
    public decimal? Quantity { get; init; }

    public DateTimeOffset? TransactionDate { get; init; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, ErrorMessage = "Reason must not exceed 500 characters.")]
    public string? Reason { get; init; }
}
