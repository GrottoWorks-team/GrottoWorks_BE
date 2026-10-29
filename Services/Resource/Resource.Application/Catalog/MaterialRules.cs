using Resource.Domain;
using Resource.Domain.Catalog;

namespace Resource.Application.Catalog;

/// <summary>Catalog checks shared by every document that names materials (purchase lines, donation items).</summary>
public static class MaterialRules
{
    /// <summary>Every material exists (422 <c>MATERIAL_UNKNOWN</c>) and is ACTIVE (422 <c>MATERIAL_INACTIVE</c>).</summary>
    public static async Task<IReadOnlyDictionary<Guid, Material>> EnsureActiveAsync(
        IMaterialCatalogStore catalog,
        IEnumerable<Guid> materialIds,
        CancellationToken cancellationToken)
    {
        var ids = materialIds.Distinct().ToList();
        var materials = (await catalog.GetMaterialsAsync(ids, cancellationToken)).ToDictionary(material => material.MaterialId);
        foreach (var id in ids)
        {
            if (!materials.TryGetValue(id, out var material))
            {
                throw DomainRuleException.Validation("MATERIAL_UNKNOWN", $"Material {id} does not exist.");
            }

            if (material.Status != CatalogStatus.Active)
            {
                throw DomainRuleException.Validation("MATERIAL_INACTIVE", $"Material {material.Code} is inactive.");
            }
        }

        return materials;
    }
}
