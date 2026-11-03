using Resource.Application.Inventory;
using Resource.Contracts.Inventory;
using Resource.Domain;
using Resource.Domain.Catalog;

namespace Resource.UnitTests.Inventory;

public sealed class SupportReconciliationQueryTests
{
    [Fact]
    public async Task Availability_returns_unallocated_stock_and_zero_for_catalog_material_with_no_stock()
    {
        var scopes=new FakeScopeStore();var area=scopes.Seed(Guid.NewGuid(),Guid.NewGuid());var store=new Store();
        var first=Guid.NewGuid();var second=Guid.NewGuid();
        store.Totals.AddRange([new(first,CatalogStatus.Active,40,15),new(second,CatalogStatus.Inactive,0,0)]);
        var result=await new SupportReconciliationQueries(store,scopes,TimeProvider.System).AvailabilityAsync(area.WorkAreaId,[first,second,first]);
        result.Count.Should().Be(2);result[0].UnallocatedQuantity.Should().Be(25);result[1].UnallocatedQuantity.Should().Be(0);
        result[1].MaterialStatus.Should().Be("INACTIVE");result[0].MaterialOfficerUserId.Should().Be(area.MaterialOfficerUserId);
        result[0].CalculatedAt.Should().Be(result[1].CalculatedAt);store.Reads.Should().Be(1);
    }

    [Fact]
    public async Task Transfer_lookup_omits_unknown_ids_and_rejects_missing_ownership_context()
    {
        var store=new Store();var id=Guid.NewGuid();
        var row=new SupportTransferSnapshot(id,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),5,"IN_TRANSIT",
            Guid.NewGuid(),DateTimeOffset.UtcNow,null,null,null,null);store.Transfers.Add(row);
        var handler=new SupportReconciliationQueries(store,new FakeScopeStore(),TimeProvider.System);
        (await handler.TransfersAsync([id,Guid.NewGuid(),id])).Should().ContainSingle().Which.Should().Be(row);
        store.Transfers[0]=row with {ParishId=Guid.Empty};
        Func<Task> read=()=>handler.TransfersAsync([id]);
        await read.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Kind==DomainErrorKind.ServiceUnavailable);
    }

    [Fact]
    public async Task Invalid_batch_is_rejected_before_querying()
    {
        var store=new Store();var handler=new SupportReconciliationQueries(store,new FakeScopeStore(),TimeProvider.System);
        foreach(var ids in new[] {Array.Empty<Guid>(),new[] {Guid.Empty},Enumerable.Range(0,101).Select(_=>Guid.NewGuid()).ToArray()})
        {
            Func<Task> read=()=>handler.TransfersAsync(ids);
            await read.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Kind==DomainErrorKind.BadRequest);
        }
        store.Reads.Should().Be(0);
    }

    private sealed class Store : ISupportReconciliationReadStore
    {
        public List<SupportTransferSnapshot> Transfers {get;}=[];public List<AvailableMaterialTotals> Totals {get;}=[];public int Reads {get;private set;}
        public Task<IReadOnlyList<SupportTransferSnapshot>> GetTransfersAsync(IReadOnlyCollection<Guid> ids,CancellationToken ct)
        {Reads++;return Task.FromResult<IReadOnlyList<SupportTransferSnapshot>>(Transfers.Where(row=>ids.Contains(row.SupportResponseMaterialId)).ToList());}
        public Task<IReadOnlyList<AvailableMaterialTotals>> GetAvailabilityAsync(Guid id,IReadOnlyCollection<Guid> materials,CancellationToken ct)
        {Reads++;return Task.FromResult<IReadOnlyList<AvailableMaterialTotals>>(Totals.Where(row=>materials.Contains(row.MaterialId)).ToList());}
    }
}
