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
