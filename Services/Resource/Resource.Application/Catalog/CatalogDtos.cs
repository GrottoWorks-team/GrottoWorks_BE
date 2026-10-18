using Resource.Domain.Catalog;

namespace Resource.Application.Catalog;

public sealed record MaterialCategoryDto(Guid Id, string Code, string Name, string? Description, CatalogStatus Status)
{
    public static MaterialCategoryDto From(MaterialCategory category) =>
        new(category.MaterialCategoryId, category.Code, category.Name, category.Description, category.Status);
}

public sealed record MaterialDto(
    Guid Id,
    Guid CategoryId,
    string Code,
    string Name,
    string Unit,
    string? Description,
    CatalogStatus Status)
{
    public static MaterialDto From(Material material) =>
        new(
            material.MaterialId,
            material.MaterialCategoryId,
            material.Code,
            material.Name,
            material.Unit,
            material.Description,
            material.Status);
}

public enum MaterialSortField
{
    Name,
    Code
}

/// <summary><c>MaterialFilter</c> of <c>listMaterials</c>.</summary>
public sealed record MaterialSearchCriteria(
    int Page,
    int Size,
    Guid? CategoryId = null,
    CatalogStatus? Status = null,
    string? Search = null,
    MaterialSortField SortField = MaterialSortField.Name,
    bool SortDescending = false);
