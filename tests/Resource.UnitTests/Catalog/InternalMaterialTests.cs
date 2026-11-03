using Resource.Application.Catalog;
using Resource.Domain;
using Resource.Domain.Catalog;

namespace Resource.UnitTests.Catalog;

public sealed class InternalMaterialTests
{
    [Fact]
    public async Task Batch_omits_unknown_ids_deduplicates_and_preserves_inactive_status()
    {
        var store = new FakeCatalogStore();
        var category = MaterialCategory.Create("TOOLS", "Tools", null);
        var active = Material.Create(category, "HAMMER", "Hammer", "piece", null);
        var inactive = Material.Create(category, "OLD", "Old tool", "piece", null);
        inactive.Update(null, null, null, CatalogStatus.Inactive, false);
        store.Materials.AddRange([active, inactive]);

        var result = await new GetInternalMaterialsHandler(store).HandleAsync([inactive.MaterialId, Guid.NewGuid(), active.MaterialId, active.MaterialId]);

        result.Select(row => row.MaterialId).Should().Equal(inactive.MaterialId, active.MaterialId);
        result[0].Status.Should().Be("INACTIVE");
        result[1].Unit.Should().Be("piece");
    }

    [Fact]
    public async Task Empty_or_oversized_batches_are_bad_requests()
    {
        var handler = new GetInternalMaterialsHandler(new FakeCatalogStore());
        foreach (var ids in new[] { Array.Empty<Guid>(), new[] { Guid.Empty }, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() })
        {
            Func<Task> query = () => handler.HandleAsync(ids);
            (await query.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.BadRequest);
        }
    }
}
