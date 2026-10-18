using System.ComponentModel.DataAnnotations;
using Resource.Domain.Catalog;

namespace Resource.Api.Requests;

/// <summary><c>CreateCategoryRequest</c> (F-RES-02).</summary>
public sealed class CreateMaterialCategoryRequest
{
    [Required(ErrorMessage = "Code is required.")]
    [StringLength(MaterialCategory.MaxCodeLength, ErrorMessage = "Code must not exceed 50 characters.")]
    public string? Code { get; init; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(MaterialCategory.MaxNameLength, ErrorMessage = "Name must not exceed 100 characters.")]
    public string? Name { get; init; }

    [StringLength(1000, ErrorMessage = "Description must not exceed 1000 characters.")]
    public string? Description { get; init; }
}

/// <summary><c>UpdateCategoryRequest</c> — PATCH: omitted = unchanged; status INACTIVE deactivates.</summary>
public sealed class UpdateMaterialCategoryRequest
{
    [StringLength(MaterialCategory.MaxNameLength, ErrorMessage = "Name must not exceed 100 characters.")]
    public string? Name { get; init; }

    /// <summary>Send an empty string to clear the description.</summary>
    [StringLength(1000, ErrorMessage = "Description must not exceed 1000 characters.")]
    public string? Description { get; init; }

    public CatalogStatus? Status { get; init; }
}

/// <summary><c>CreateMaterialRequest</c> (F-RES-02). <c>unit</c> is the unit of every quantity of this material.</summary>
public sealed class CreateMaterialRequest
{
    [Required(ErrorMessage = "Category is required.")]
    public Guid? CategoryId { get; init; }

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(Material.MaxCodeLength, ErrorMessage = "Code must not exceed 50 characters.")]
    public string? Code { get; init; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(Material.MaxNameLength, ErrorMessage = "Name must not exceed 150 characters.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Unit is required.")]
    [StringLength(Material.MaxUnitLength, ErrorMessage = "Unit must not exceed 30 characters.")]
    public string? Unit { get; init; }

    [StringLength(2000, ErrorMessage = "Description must not exceed 2000 characters.")]
    public string? Description { get; init; }
}

/// <summary><c>UpdateMaterialRequest</c> — PATCH; the unit is locked once quantities were recorded.</summary>
public sealed class UpdateMaterialRequest
{
    public Guid? CategoryId { get; init; }

    [StringLength(Material.MaxNameLength, ErrorMessage = "Name must not exceed 150 characters.")]
    public string? Name { get; init; }

    [StringLength(Material.MaxUnitLength, ErrorMessage = "Unit must not exceed 30 characters.")]
    public string? Unit { get; init; }

    [StringLength(2000, ErrorMessage = "Description must not exceed 2000 characters.")]
    public string? Description { get; init; }

    public CatalogStatus? Status { get; init; }
}
