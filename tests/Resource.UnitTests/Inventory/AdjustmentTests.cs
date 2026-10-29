using BuildingBlocks.Security;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Inventory;

/// <summary>F-RES-16: inventory adjustments — damaged/lost/disposed leave usable stock (BR-43), never above unallocated.</summary>
public sealed class AdjustmentTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakeStockLock _lock = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _area;
    private readonly Material _cable;

    public AdjustmentTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        _cable = _catalog.SeedMaterial(_catalog.SeedCategory(), "CABLE", "Dây điện", "m");
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 50, 0, null, DateTimeOffset.UtcNow));
        _inventory.Append(MaterialTransaction.Post(_area.WorkAreaId, _cable.MaterialId, TransactionType.OpeningStock, 40, DateTimeOffset.UtcNow, _officer, null));
    }

    private InventoryReader Reader() => new(_inventory, _catalog);

    private CreateInventoryAdjustmentCommandHandler Adjust(Guid? officer = null)
    {
        var user = new FakeCurrentUser(officer ?? _officer, GrottoWorksRoles.MaterialOfficer, Parish);
        return new(new WorkAreaAccess(_scopes, user), _catalog, Reader(), _inventory, _lock, new ShortageTracker(Reader(), _events), user);
    }

    private CreateInventoryAdjustmentCommand Command(AdjustmentKind kind, decimal quantity, string? reason = "Mưa làm hỏng") =>
        new(_area.WorkAreaId, _cable.MaterialId, kind, quantity, null, reason);

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Theory]
    [InlineData(AdjustmentKind.Damaged, TransactionType.AdjustmentDamaged)]
    [InlineData(AdjustmentKind.Lost, TransactionType.AdjustmentLost)]
    [InlineData(AdjustmentKind.Disposed, TransactionType.AdjustmentDisposed)]
    public async Task Adjustment_posts_a_negative_ledger_row_referencing_itself(AdjustmentKind kind, TransactionType type)
    {
        var dto = await Adjust().HandleAsync(Command(kind, 4));

        dto.TransactionType.Should().Be(type);
        dto.Quantity.Should().Be(-4);
        dto.ReferenceType.Should().Be(TransactionReferenceType.InventoryAdjustment);
        dto.ReferenceId.Should().NotBeEmpty();
        dto.Note.Should().Be("Mưa làm hỏng");
        var row = (await Reader().ReadAsync([_area], _cable.MaterialId)).Single();
        row.ConfirmedAvailable.Should().Be(36);
        row.Shortage.Should().Be(14);
        _events.Events.Single().Payload.Should().BeOfType<MaterialShortageChanged>().Which.Shortage.Should().Be(14);
        _lock.Committed.Should().Be(1);
    }

    [Fact]
    public async Task Only_unallocated_stock_can_be_written_off()
    {
        _inventory.Allocations.Add(MaterialAllocation.Create(_area.WorkAreaId, null, [new AllocationLineInput(_cable.MaterialId, 35)],
            DateTimeOffset.UtcNow, null, _officer, DateTimeOffset.UtcNow));

        (await Fails(() => Adjust().HandleAsync(Command(AdjustmentKind.Lost, 6)))).Code.Should().Be("MATERIAL_INSUFFICIENT_AVAILABLE");
        (await Adjust().HandleAsync(Command(AdjustmentKind.Lost, 5))).Quantity.Should().Be(-5);
        _lock.Committed.Should().Be(1);
    }

    [Fact]
    public async Task Reason_quantity_and_scope_are_checked()
    {
        (await Fails(() => Adjust().HandleAsync(Command(AdjustmentKind.Damaged, 1, " ")))).Code.Should().Be("ADJUSTMENT_REASON_REQUIRED");
        (await Fails(() => Adjust().HandleAsync(Command(AdjustmentKind.Damaged, 0)))).Code.Should().Be("TRANSACTION_QUANTITY_INVALID");
        (await Fails(() => Adjust().HandleAsync(Command(AdjustmentKind.Damaged, 1) with { TransactionDate = DateTimeOffset.UtcNow.AddDays(1) })))
            .Code.Should().Be("TRANSACTION_DATE_IN_FUTURE");
        (await Fails(() => Adjust().HandleAsync(Command(AdjustmentKind.Damaged, 1) with { MaterialId = Guid.NewGuid() }))).Code.Should().Be("MATERIAL_UNKNOWN");
        (await Fails(() => Adjust(Guid.NewGuid()).HandleAsync(Command(AdjustmentKind.Damaged, 1)))).Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        _inventory.Ledger.Should().ContainSingle();
    }
}
