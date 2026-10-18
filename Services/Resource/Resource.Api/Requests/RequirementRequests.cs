using System.ComponentModel.DataAnnotations;
using Resource.Domain.Inventory;

namespace Resource.Api.Requests;

/// <summary><c>CreateMaterialRequirementRequest</c> (F-RES-03). Quantities are in the material's unit.</summary>
public sealed class CreateMaterialRequirementRequest
{
    [Required(ErrorMessage = "Material is required.")]
    public Guid? MaterialId { get; init; }

    [Required(ErrorMessage = "Required quantity is required.")]
    public decimal? RequiredQuantity { get; init; }

    /// <summary>Display only — shortage is computed from the stock ledger.</summary>
    public decimal? ExistingQuantity { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>UpdateMaterialRequirementRequest</c> — PATCH; <c>status</c> CANCELLED / ACTIVE replaces deletion.</summary>
public sealed class UpdateMaterialRequirementRequest
{
    public long? Version { get; init; }

    public decimal? RequiredQuantity { get; init; }

    public decimal? ExistingQuantity { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }

    public RequirementStatus? Status { get; init; }
}
