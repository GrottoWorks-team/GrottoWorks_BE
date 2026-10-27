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
