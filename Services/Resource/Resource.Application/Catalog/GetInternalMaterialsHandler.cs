using Resource.Contracts.Catalog;
using Resource.Domain;

namespace Resource.Application.Catalog;

public sealed class GetInternalMaterialsHandler(IMaterialCatalogStore store)
{
    public async Task<IReadOnlyList<InternalMaterialDto>> HandleAsync(IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default)
    {
        if (materialIds.Count is < 1 or > 100 || materialIds.Contains(Guid.Empty))
            throw new DomainRuleException("INVALID_MATERIAL_IDS", "Supply between 1 and 100 non-empty material IDs.", DomainErrorKind.BadRequest);
        var ids = materialIds.Distinct().ToArray();
        var materials = (await store.GetMaterialsAsync(ids, cancellationToken)).ToDictionary(material => material.MaterialId);
        // Unknown IDs are omitted so callers can report which references do not exist.
        return ids.Where(materials.ContainsKey).Select(id =>
        {
            var material = materials[id];
            return new InternalMaterialDto(material.MaterialId, material.Code, material.Name, material.Unit,
                material.Status.ToString().ToUpperInvariant());
        }).ToArray();
    }
}
