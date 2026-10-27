using System.ComponentModel.DataAnnotations;
using Resource.Domain.Contributions;

namespace Resource.Api.Requests;

/// <summary>One material actually received; <c>unusableQuantity</c> (default 0) is not posted to stock.</summary>
public sealed class ReceivedMaterialRequest
{
    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Received quantity is required.")]
    public decimal? ReceivedQuantity { get; init; }

    public decimal? UnusableQuantity { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary>
/// <c>CreateDonationRequest</c> (F-RES-13 <c>recordDirectDonation</c>): MONEY → <c>amount</c> (VND) and a season or
/// work area; MATERIAL → <c>workAreaId</c> and <c>items</c>. <c>receivedAt</c> defaults to now.
/// </summary>
public sealed class CreateDonationRequest
{
    [Required(ErrorMessage = "Donor is required.")]
    public Guid? DonorId { get; init; }

    [Required(ErrorMessage = "Type is required.")]
    public DonationType? Type { get; init; }

    public Guid? SeasonId { get; init; }

    public Guid? WorkAreaId { get; init; }

    public decimal? Amount { get; init; }

    public List<ReceivedMaterialRequest>? Items { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

public sealed class PledgedMaterialRequest
{
    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Pledged quantity is required.")]
    public decimal? PledgedQuantity { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>MaterialPledgeRequest</c> (F-RES-13b): materials promised to a work area.</summary>
public sealed class MaterialPledgeRequest
{
    [Required(ErrorMessage = "Work area is required.")]
    public Guid? WorkAreaId { get; init; }

    [Required(ErrorMessage = "Items are required.")]
    public List<PledgedMaterialRequest>? Items { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>MonetaryPledgeRequest</c> (F-RES-13b): money promised for a season (VND), optionally a work area.</summary>
public sealed class MonetaryPledgeRequest
{
    [Required(ErrorMessage = "Season is required.")]
    public Guid? SeasonId { get; init; }

    public Guid? WorkAreaId { get; init; }

    [Required(ErrorMessage = "Amount is required.")]
    public decimal? Amount { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}
