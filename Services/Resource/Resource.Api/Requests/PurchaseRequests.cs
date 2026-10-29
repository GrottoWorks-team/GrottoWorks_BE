using System.ComponentModel.DataAnnotations;

namespace Resource.Api.Requests;

/// <summary>One line of <c>CreatePurchaseRequest</c> / <c>UpdatePurchaseRequest</c>. Cost is the whole line, in VND.</summary>
public sealed class PurchaseRequestItemRequest
{
    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Requested quantity is required.")]
    public decimal? RequestedQuantity { get; init; }

    public decimal? EstimatedCost { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }

    /// <summary>Required when the line asks for more than the uncovered shortage (BA 8.6).</summary>
    [StringLength(500, ErrorMessage = "Shortage override reason must not exceed 500 characters.")]
    public string? ShortageOverrideReason { get; init; }
}

/// <summary><c>CreatePurchaseRequest</c> (F-RES-06): a PLANNING request; reason and at least one item are required on submit.</summary>
public sealed class CreatePurchaseRequestRequest
{
    [Required(ErrorMessage = "Work area is required.")]
    public Guid? WorkAreaId { get; init; }

    [StringLength(2000, ErrorMessage = "Reason must not exceed 2000 characters.")]
    public string? Reason { get; init; }

    public List<PurchaseRequestItemRequest>? Items { get; init; }
}

/// <summary><c>UpdatePurchaseRequest</c> — PATCH of a PLANNING request; <c>items</c>, when given, replaces every line.</summary>
public sealed class UpdatePurchaseRequestRequest
{
    public long? Version { get; init; }

    [StringLength(2000, ErrorMessage = "Reason must not exceed 2000 characters.")]
    public string? Reason { get; init; }

    public List<PurchaseRequestItemRequest>? Items { get; init; }
}

/// <summary><c>VersionedCommand</c>.</summary>
public sealed class VersionedCommandRequest
{
    public long? Version { get; init; }
}

/// <summary><c>ApprovalCommand</c>: version and an optional note.</summary>
public sealed class ApprovalCommandRequest
{
    public long? Version { get; init; }

    [StringLength(500, ErrorMessage = "Note must not exceed 500 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>ReasonCommand</c>: version and a required reason of 1–500 characters.</summary>
public sealed class ReasonCommandRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Reason must be 1–500 characters.")]
    public string? Reason { get; init; }
}

public sealed class PurchaseLineRequest
{
    [Required(ErrorMessage = "Purchase request line is required.")]
    public Guid? PurchaseRequestItemId { get; init; }

    [Required(ErrorMessage = "Purchased quantity is required.")]
    public decimal? PurchasedQuantity { get; init; }

    [Required(ErrorMessage = "Unit price is required.")]
    public decimal? UnitPrice { get; init; }

    /// <summary>Defaults to quantity × unit price.</summary>
    public decimal? TotalPrice { get; init; }
}

/// <summary><c>FileReference</c> (only the id is needed to attach an uploaded file).</summary>
public sealed class FileReferenceRequest
{
    [Required(ErrorMessage = "File id is required.")]
    public Guid? Id { get; init; }
}

/// <summary><c>CreatePurchaseBody</c> (F-RES-09): what the buyer actually bought; no stock change yet (BR-26).</summary>
public sealed class CreatePurchaseBody
{
    [StringLength(150, ErrorMessage = "Supplier must not exceed 150 characters.")]
    public string? SupplierName { get; init; }

    public DateOnly? PurchaseDate { get; init; }

    [Required(ErrorMessage = "Items are required.")]
    public List<PurchaseLineRequest>? Items { get; init; }

    public List<FileReferenceRequest>? Receipts { get; init; }
}

public sealed class DeliveryLineRequest
{
    [Required(ErrorMessage = "Purchase line is required.")]
    public Guid? PurchaseItemId { get; init; }

    [Required(ErrorMessage = "Received usable quantity is required.")]
    public decimal? ReceivedUsableQuantity { get; init; }

    public decimal? ReceivedUnusableQuantity { get; init; }
}

/// <summary><c>ConfirmDeliveryRequest</c> (F-RES-10): usable / unusable counted per purchased line.</summary>
public sealed class ConfirmDeliveryRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Items are required.")]
    public List<DeliveryLineRequest>? Items { get; init; }
}
