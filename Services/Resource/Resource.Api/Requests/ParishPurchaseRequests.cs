using System.ComponentModel.DataAnnotations;

namespace Resource.Api.Requests;

/// <summary>One material line of a parish purchase receipt (26/11/2026).</summary>
public sealed class ParishPurchaseLineRequest
{
    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Range(0.01, 999999.99, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; init; }

    [Range(0, 999999999999.99, ErrorMessage = "Unit price cannot be negative.")]
    public decimal UnitPrice { get; init; }
}

/// <summary>
/// <c>recordParishPurchase</c> (26/11/2026): the parish committee bought materials for the central warehouse.
/// An invoice photo is mandatory and the total is computed server-side.
/// </summary>
public sealed class RecordParishPurchaseRequest
{
    [Required(ErrorMessage = "Season is required.")]
    public Guid? SeasonId { get; init; }

    [Required(ErrorMessage = "Name of the person who paid is required.")]
    [StringLength(150, MinimumLength = 1)]
    public string? BuyerName { get; init; }

    [StringLength(150)]
    public string? SupplierName { get; init; }

    public DateOnly? PurchaseDate { get; init; }

    [Required(ErrorMessage = "At least one material line is required.")]
    public List<ParishPurchaseLineRequest>? Items { get; init; }

    [Required(ErrorMessage = "Attach at least one invoice photo.")]
    public List<FileReferenceRequest>? Invoices { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }
}
