using System.ComponentModel.DataAnnotations;
using Resource.Application.Inventory;

namespace Resource.Api.Requests;

/// <summary><c>recordParishOpeningStock</c> (F-RES-21): confirmed usable quantity the parish warehouse starts a season with.</summary>
public sealed class ParishOpeningStockRequest
{
    [Required(ErrorMessage = "Season is required.")]
    public Guid? SeasonId { get; init; }

    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Quantity is required.")]
    public decimal? Quantity { get; init; }

    public DateTimeOffset? TransactionDate { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>createParishInventoryAdjustment</c> (F-RES-21): damaged, lost or disposed stock leaves the warehouse.</summary>
public sealed class ParishInventoryAdjustmentRequest
{
    [Required(ErrorMessage = "Season is required.")]
    public Guid? SeasonId { get; init; }

    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Adjustment type is required.")]
    public AdjustmentKind? Type { get; init; }

    [Required(ErrorMessage = "Quantity is required.")]
    public decimal? Quantity { get; init; }

    public DateTimeOffset? TransactionDate { get; init; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, ErrorMessage = "Reason must not exceed 500 characters.")]
    public string? Reason { get; init; }
}

public sealed class ParishDistributionLineRequest
{
    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Quantity is required.")]
    public decimal? Quantity { get; init; }
}

/// <summary><c>createParishDistribution</c> (F-RES-21).</summary>
public sealed class CreateParishDistributionRequest
{
    [Required(ErrorMessage = "Season is required.")]
    public Guid? SeasonId { get; init; }

    [Required(ErrorMessage = "Target work area is required.")]
    public Guid? TargetWorkAreaId { get; init; }

    [Required(ErrorMessage = "Items are required.")]
    public List<ParishDistributionLineRequest>? Items { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

public sealed class ParishDistributionReceiptLineRequest
{
    [Required(ErrorMessage = "Distribution item is required.")]
    public Guid? ItemId { get; init; }

    [Required(ErrorMessage = "Received usable quantity is required.")]
    public decimal? UsableQuantity { get; init; }

    public decimal? UnusableQuantity { get; init; }
}

/// <summary><c>confirmParishDistributionReceipt</c> (F-RES-21).</summary>
public sealed class ConfirmParishDistributionRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Items are required.")]
    public List<ParishDistributionReceiptLineRequest>? Items { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>cancelParishDistribution</c> (F-RES-21).</summary>
public sealed class CancelParishDistributionRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, ErrorMessage = "Reason must not exceed 500 characters.")]
    public string? Reason { get; init; }
}
