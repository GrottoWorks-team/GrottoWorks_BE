using Resource.Application.Inventory;
using Resource.Application.Requirements;

namespace Resource.Infrastructure.Requirements;

public sealed class MaterialRequirementStore(IInventoryStore inventory) : IMaterialRequirementStore
{
    public Task<IReadOnlyList<QuantityByMaterial>> ListByWorkAreaAsync(
        Guid workAreaId, CancellationToken cancellationToken = default) =>
        inventory.GetRequiredTotalsAsync([workAreaId], null, cancellationToken);
}
