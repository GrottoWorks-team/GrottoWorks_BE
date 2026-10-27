using BuildingBlocks.Security;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Inventory;

/// <summary>F-RES-15: allocations — never above unallocated (BR-33), under the stock lock, task of the same work area.</summary>
public sealed class AllocationTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakeTaskProjectionStore _tasks = new();
    private readonly FakeStockLock _lock = new();
    private readonly WorkAreaScope _area;
    private readonly Material _cable;
    private readonly Material _paint;

    public AllocationTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        var category = _catalog.SeedCategory();
        _cable = _catalog.SeedMaterial(category, "CABLE", "Dây điện", "m");
        _paint = _catalog.SeedMaterial(category, "PAINT", "Sơn", "lít");
        _inventory.Append(MaterialTransaction.Post(_area.WorkAreaId, _cable.MaterialId, TransactionType.OpeningStock, 100, DateTimeOffset.UtcNow, _officer, null));
    }

    private FakeCurrentUser Officer(Guid? id = null) => new(id ?? _officer, GrottoWorksRoles.MaterialOfficer, Parish);

    private InventoryReader Reader() => new(_inventory, _catalog);

    private AllocationProjector Projector() => new(_catalog, _scopes, _tasks);

    private AllocateMaterialCommandHandler Allocate(FakeCurrentUser? user = null)
    {
        var current = user ?? Officer();
        return new(new WorkAreaAccess(_scopes, current), _tasks, _catalog, Reader(), new FakeAllocationStore(_inventory), _lock, Projector(), current);
    }

    private AllocateMaterialCommand Cable(decimal quantity, Guid? taskId = null) =>
        new(_area.WorkAreaId, taskId, [new AllocationLineInput(_cable.MaterialId, quantity)], null, "Cho hang đá");

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Allocation_reduces_unallocated_but_not_confirmed_available_nor_shortage()
    {
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 120, 0, null, DateTimeOffset.UtcNow));
        var task = _tasks.Seed(_area.WorkAreaId);

        var dto = await Allocate().HandleAsync(Cable(30, task.TaskId));

        dto.TaskId.Should().Be(task.TaskId);
        dto.TaskName.Should().Be("Treo đèn");
        dto.WorkAreaName.Should().Be(_area.Name);
        dto.Items.Single().AllocatedQuantity.Should().Be(30);
        dto.Items.Single().Unit.Should().Be("m");
        var row = (await Reader().ReadAsync([_area], _cable.MaterialId)).Single();
        row.ConfirmedAvailable.Should().Be(100);
        row.Allocated.Should().Be(30);
        row.Unallocated.Should().Be(70);
        row.Shortage.Should().Be(20);
        _lock.Acquired.Should().Be(1);
        _lock.Committed.Should().Be(1);
    }

    [Fact]
    public async Task Allocations_never_exceed_unallocated_stock()
    {
        // BA 8.9 example: 100 m → 30 + 40 + 20, 10 left.
        await Allocate().HandleAsync(Cable(30));
        await Allocate().HandleAsync(Cable(40));
        await Allocate().HandleAsync(Cable(20));

        var failure = await Fails(() => Allocate().HandleAsync(Cable(10.5m)));

        failure.Code.Should().Be("MATERIAL_INSUFFICIENT_AVAILABLE");
        failure.Kind.Should().Be(DomainErrorKind.Conflict);
        (await Allocate().HandleAsync(Cable(10))).Items.Single().AllocatedQuantity.Should().Be(10);
        (await Reader().ReadAsync([_area], _cable.MaterialId)).Single().Unallocated.Should().Be(0);
        _lock.Committed.Should().Be(4);
    }

    [Fact]
    public async Task Material_without_stock_cannot_be_allocated_and_a_failed_line_rolls_back_the_whole_allocation()
    {
        var both = new AllocateMaterialCommand(_area.WorkAreaId, null,
            [new AllocationLineInput(_cable.MaterialId, 5), new AllocationLineInput(_paint.MaterialId, 1)], null, null);

        (await Fails(() => Allocate().HandleAsync(both))).Code.Should().Be("MATERIAL_INSUFFICIENT_AVAILABLE");
        _inventory.Allocations.Should().BeEmpty();
        _lock.Committed.Should().Be(0);
    }

    [Fact]
    public async Task Task_must_be_open_and_in_the_same_work_area()
    {
        var otherArea = _scopes.Seed(Parish, _officer);
        var foreignTask = _tasks.Seed(otherArea.WorkAreaId);
        var done = _tasks.Seed(_area.WorkAreaId, ProjectedTaskStatus.Completed);

        (await Fails(() => Allocate().HandleAsync(Cable(1, foreignTask.TaskId)))).Code.Should().Be("TASK_UNKNOWN");
        (await Fails(() => Allocate().HandleAsync(Cable(1, Guid.NewGuid())))).Code.Should().Be("TASK_UNKNOWN");
        (await Fails(() => Allocate().HandleAsync(Cable(1, done.TaskId)))).Code.Should().Be("TASK_NOT_ALLOCATABLE");
    }

    [Fact]
    public async Task Lines_are_validated_and_only_the_officer_of_the_area_allocates()
    {
        (await Fails(() => Allocate().HandleAsync(Cable(0)))).Code.Should().Be("ALLOCATION_QUANTITY_INVALID");
        (await Fails(() => Allocate().HandleAsync(new AllocateMaterialCommand(_area.WorkAreaId, null, [], null, null))))
            .Code.Should().Be("ALLOCATION_ITEMS_REQUIRED");
        (await Fails(() => Allocate().HandleAsync(new AllocateMaterialCommand(_area.WorkAreaId, null,
            [new AllocationLineInput(_cable.MaterialId, 1), new AllocationLineInput(_cable.MaterialId, 2)], null, null))))
            .Code.Should().Be("ALLOCATION_DUPLICATE_MATERIAL");
        (await Fails(() => Allocate(Officer(Guid.NewGuid())).HandleAsync(Cable(1)))).Code.Should().Be("WORK_AREA_SCOPE_DENIED");
    }

    [Fact]
    public async Task List_filters_by_task_and_material_within_the_parish()
    {
        var task = _tasks.Seed(_area.WorkAreaId);
        await Allocate().HandleAsync(Cable(10, task.TaskId));
        await Allocate().HandleAsync(Cable(5));

        var leader = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Leader, Parish);
        var list = new ListMaterialAllocationsQueryHandler(new WorkAreaAccess(_scopes, leader), _scopes, new FakeAllocationStore(_inventory), Projector());

        (await list.HandleAsync(new AllocationFilter(1, 20, null, null, null, null))).TotalItems.Should().Be(2);
        (await list.HandleAsync(new AllocationFilter(1, 20, null, null, task.TaskId, null))).Items.Should().ContainSingle()
            .Which.Items.Single().AllocatedQuantity.Should().Be(10);
        (await list.HandleAsync(new AllocationFilter(1, 20, null, null, null, _paint.MaterialId))).TotalItems.Should().Be(0);

        var outsider = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Parish, Guid.NewGuid());
        (await new ListMaterialAllocationsQueryHandler(new WorkAreaAccess(_scopes, outsider), _scopes, new FakeAllocationStore(_inventory), Projector())
            .HandleAsync(new AllocationFilter(1, 20, null, null, null, null))).TotalItems.Should().Be(0);
    }
}
