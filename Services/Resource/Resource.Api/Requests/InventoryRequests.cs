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
