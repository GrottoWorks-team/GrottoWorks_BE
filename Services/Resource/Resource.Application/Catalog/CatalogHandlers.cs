using Resource.Domain;
using Resource.Domain.Catalog;

namespace Resource.Application.Catalog;

public sealed record CreateMaterialCategoryCommand(string Code, string Name, string? Description);

public sealed record UpdateMaterialCategoryCommand(Guid CategoryId, string? Name, string? Description, CatalogStatus? Status);

public sealed record CreateMaterialCommand(Guid CategoryId, string Code, string Name, string Unit, string? Description);

public sealed record UpdateMaterialCommand(
    Guid MaterialId,
    Guid? CategoryId,
    string? Name,
    string? Unit,
    string? Description,
    CatalogStatus? Status);

/// <summary>F-RES-02 <c>listMaterialCategories</c> (USER). Defaults to ACTIVE ("List active material categories").</summary>
public sealed class ListMaterialCategoriesQueryHandler(IMaterialCatalogStore store)
{
    public async Task<IReadOnlyList<MaterialCategoryDto>> HandleAsync(
        CatalogStatus? status,
        CancellationToken cancellationToken = default)
    {
        var categories = await store.ListCategoriesAsync(status ?? CatalogStatus.Active, cancellationToken);
        return categories.Select(MaterialCategoryDto.From).ToList();
    }
}

/// <summary>F-RES-02 <c>createMaterialCategory</c> (MO): unique upper-case code.</summary>
public sealed class CreateMaterialCategoryCommandHandler(IMaterialCatalogStore store)
{
    public async Task<MaterialCategoryDto> HandleAsync(
        CreateMaterialCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var category = MaterialCategory.Create(command.Code, command.Name, command.Description);

        if (await store.CategoryCodeExistsAsync(category.Code, cancellationToken))
        {
            throw new DomainRuleException(
                "MATERIAL_CATEGORY_CODE_ALREADY_EXISTS",
                "A material category with this code already exists.");
        }

        store.AddCategory(category);
        await store.SaveChangesAsync(cancellationToken);
        return MaterialCategoryDto.From(category);
    }
}

/// <summary>
/// F-RES-02 <c>updateMaterialCategory</c> (MO): rename or deactivate. A category with ACTIVE materials
/// cannot be deactivated — deactivate (or move) its materials first.
/// </summary>
public sealed class UpdateMaterialCategoryCommandHandler(IMaterialCatalogStore store)
{
    public async Task<MaterialCategoryDto> HandleAsync(
        UpdateMaterialCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var category = await store.GetCategoryAsync(command.CategoryId, cancellationToken)
            ?? throw DomainRuleException.NotFound("MATERIAL_CATEGORY_NOT_FOUND", "The material category does not exist.");

        if (command.Status == CatalogStatus.Inactive &&
            category.Status == CatalogStatus.Active &&
            await store.CategoryHasActiveMaterialsAsync(category.MaterialCategoryId, cancellationToken))
        {
            throw new DomainRuleException(
                "MATERIAL_CATEGORY_HAS_ACTIVE_MATERIALS",
                "Deactivate or move the active materials of this category first.");
        }

        category.Update(command.Name, command.Description, command.Status);
        await store.SaveChangesAsync(cancellationToken);
        return MaterialCategoryDto.From(category);
    }
}

/// <summary>F-RES-02 <c>listMaterials</c> (USER): paged catalog with category/status/text filters.</summary>
public sealed class ListMaterialsQueryHandler(IMaterialCatalogStore store)
{
    public async Task<PagedResult<MaterialDto>> HandleAsync(
        MaterialSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var result = await store.SearchMaterialsAsync(criteria, cancellationToken);
        return new PagedResult<MaterialDto>(result.Items.Select(MaterialDto.From).ToList(), result.TotalItems);
    }
}

/// <summary>F-RES-02 <c>createMaterial</c> (MO): unique code, active category, unit of measure required.</summary>
public sealed class CreateMaterialCommandHandler(IMaterialCatalogStore store)
{
    public async Task<MaterialDto> HandleAsync(
        CreateMaterialCommand command,
        CancellationToken cancellationToken = default)
    {
        var category = await store.GetCategoryAsync(command.CategoryId, cancellationToken)
            ?? throw DomainRuleException.Validation("MATERIAL_CATEGORY_UNKNOWN", "The material category does not exist.");

        var material = Material.Create(category, command.Code, command.Name, command.Unit, command.Description);

        if (await store.MaterialCodeExistsAsync(material.Code, cancellationToken))
        {
            throw new DomainRuleException("MATERIAL_CODE_ALREADY_EXISTS", "A material with this code already exists.");
        }

        store.AddMaterial(material);
        await store.SaveChangesAsync(cancellationToken);
        return MaterialDto.From(material);
    }
}

/// <summary>F-RES-02 <c>updateMaterial</c> (MO): unit locked once quantities exist; deactivate instead of delete.</summary>
public sealed class UpdateMaterialCommandHandler(IMaterialCatalogStore store)
{
    public async Task<MaterialDto> HandleAsync(
        UpdateMaterialCommand command,
        CancellationToken cancellationToken = default)
    {
        var material = await store.GetMaterialAsync(command.MaterialId, cancellationToken)
            ?? throw DomainRuleException.NotFound("MATERIAL_NOT_FOUND", "The material does not exist.");

        if (command.CategoryId is { } categoryId && categoryId != material.MaterialCategoryId)
        {
            var category = await store.GetCategoryAsync(categoryId, cancellationToken)
                ?? throw DomainRuleException.Validation("MATERIAL_CATEGORY_UNKNOWN", "The material category does not exist.");
            material.MoveToCategory(category);
        }

        var unitChanges = command.Unit is not null &&
                          !string.Equals(command.Unit.Trim(), material.Unit, StringComparison.Ordinal);
        var isReferenced = unitChanges && await store.IsMaterialReferencedAsync(material.MaterialId, cancellationToken);

        material.Update(command.Name, command.Unit, command.Description, command.Status, isReferenced);
        await store.SaveChangesAsync(cancellationToken);
        return MaterialDto.From(material);
    }
}
