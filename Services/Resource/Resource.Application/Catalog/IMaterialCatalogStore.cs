using Resource.Domain.Catalog;

namespace Resource.Application.Catalog;

/// <summary>Material category + material store (F-RES-02). Narrow on purpose — no generic repository.</summary>
public interface IMaterialCatalogStore
{
    Task<IReadOnlyList<MaterialCategory>> ListCategoriesAsync(CatalogStatus? status, CancellationToken cancellationToken = default);

    Task<MaterialCategory?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    Task<bool> CategoryCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    Task<bool> CategoryHasActiveMaterialsAsync(Guid categoryId, CancellationToken cancellationToken = default);

    void AddCategory(MaterialCategory category);

    Task<PagedResult<Material>> SearchMaterialsAsync(MaterialSearchCriteria criteria, CancellationToken cancellationToken = default);

    Task<Material?> GetMaterialAsync(Guid materialId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Material>> GetMaterialsAsync(IReadOnlyCollection<Guid> materialIds, CancellationToken cancellationToken = default);

    Task<bool> MaterialCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    /// <summary>True once requirements or ledger rows point at the material (its unit is then locked).</summary>
    Task<bool> IsMaterialReferencedAsync(Guid materialId, CancellationToken cancellationToken = default);

    void AddMaterial(Material material);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
