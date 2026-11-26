using System.ComponentModel.DataAnnotations;

namespace Resource.Api.Requests;

/// <summary>One material line of an emergency purchase recorded by a volunteer during a task.</summary>
public sealed class EmergencyPurchaseLineRequest
{
    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Range(0.01, 999999.99, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; init; }

    [Range(0, 999999999999.99, ErrorMessage = "Unit price cannot be negative.")]
    public decimal UnitPrice { get; init; }
}

/// <summary>
/// <c>RecordEmergencyPurchase</c> (26/11/2026): a volunteer assigned to a running task bought materials themselves
/// because they ran out. Total must stay under 500,000 VND and an invoice photo is mandatory.
/// </summary>
public sealed class RecordEmergencyPurchaseRequest
{
    [Required(ErrorMessage = "Name of the person who paid is required.")]
    [StringLength(150, MinimumLength = 1)]
    public string? BuyerName { get; init; }

    [StringLength(150)]
    public string? SupplierName { get; init; }

    public DateOnly? PurchaseDate { get; init; }

    [Required(ErrorMessage = "At least one material line is required.")]
    public List<EmergencyPurchaseLineRequest>? Items { get; init; }

    [Required(ErrorMessage = "Attach at least one invoice photo.")]
    public List<FileReferenceRequest>? Invoices { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }
}
