using BuildingBlocks.Security;
using Resource.Application.Inventory;
using Resource.Application.Requirements;
using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Inventory;

/// <summary>F-RES-04: opening stock, balances per work area (BR-24), material history.</summary>
public sealed class InventoryHandlerTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly WorkAreaScope _grotto;
    private readonly WorkAreaScope _church;
    private readonly Material _cable;

    public InventoryHandlerTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        var season = Guid.NewGuid();
        _grotto = _scopes.Seed(Parish, _officer, season, "Hang đá");
        _church = _scopes.Seed(Parish, Guid.NewGuid(), season, "Nhà thờ");
        _cable = _catalog.SeedMaterial(_catalog.SeedCategory(), "CABLE", "Dây điện", "m");
    }

    private WorkAreaAccess Access(Guid? user = null, string role = GrottoWorksRoles.MaterialOfficer, Guid? parish = null) =>
        new(_scopes, new FakeCurrentUser(user ?? _officer, role, parish ?? Parish));

    private readonly FakeIntegrationEventPublisher _events = new();

    private RecordOpeningStockCommandHandler Opening() =>
        new(Access(), new FakeCurrentUser(_officer, GrottoWorksRoles.MaterialOfficer, Parish), _inventory, _catalog,
            new Resource.Application.Shortages.ShortageTracker(new InventoryReader(_inventory, _catalog), _events));

    private ListInventoryQueryHandler List(string role = GrottoWorksRoles.Parish) =>
        new(Access(Guid.NewGuid(), role), _scopes, new InventoryReader(_inventory, _catalog));

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Opening_stock_posts_one_positive_ledger_row_with_the_actor()
    {
        var dto = await Opening().HandleAsync(new RecordOpeningStockCommand(_grotto.WorkAreaId, _cable.MaterialId, 30, null, "kiểm kê"));

        dto.TransactionType.Should().Be(TransactionType.OpeningStock);
        dto.Quantity.Should().Be(30);
        dto.CreatedByUserId.Should().Be(_officer);
        _inventory.Ledger.Should().ContainSingle();

        (await Fails(() => Opening().HandleAsync(new RecordOpeningStockCommand(_grotto.WorkAreaId, _cable.MaterialId, 5, null, null))))
            .Code.Should().Be("OPENING_STOCK_ALREADY_RECORDED");
    }

    [Fact]
    public async Task Opening_stock_is_limited_to_the_officer_of_the_work_area_and_valid_input()
    {
        (await Fails(() => Opening().HandleAsync(new RecordOpeningStockCommand(_church.WorkAreaId, _cable.MaterialId, 5, null, null))))
            .Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        (await Fails(() => Opening().HandleAsync(new RecordOpeningStockCommand(_grotto.WorkAreaId, _cable.MaterialId, -5, null, null))))
            .Code.Should().Be("TRANSACTION_QUANTITY_INVALID");
        (await Fails(() => Opening().HandleAsync(new RecordOpeningStockCommand(_grotto.WorkAreaId, _cable.MaterialId, 5, DateTimeOffset.UtcNow.AddDays(1), null))))
            .Code.Should().Be("TRANSACTION_DATE_IN_FUTURE");
        (await Fails(() => Opening().HandleAsync(new RecordOpeningStockCommand(_grotto.WorkAreaId, Guid.NewGuid(), 5, null, null))))
            .Code.Should().Be("MATERIAL_UNKNOWN");
    }

    [Fact]
    public async Task Inventory_keeps_work_areas_separate_and_computes_shortage()
    {
        _requirements.Add(MaterialRequirement.Create(_grotto.WorkAreaId, _cable.MaterialId, 100, 0, null, DateTimeOffset.UtcNow));
        _requirements.Add(MaterialRequirement.Create(_church.WorkAreaId, _cable.MaterialId, 10, 0, null, DateTimeOffset.UtcNow));
        await Opening().HandleAsync(new RecordOpeningStockCommand(_grotto.WorkAreaId, _cable.MaterialId, 40, null, null));

        var result = await List().HandleAsync(new InventoryFilter(1, 20));

        result.TotalItems.Should().Be(2);
        var church = result.Items.Single(row => row.WorkAreaId == _church.WorkAreaId);
        var grotto = result.Items.Single(row => row.WorkAreaId == _grotto.WorkAreaId);
        grotto.ConfirmedAvailable.Should().Be(40);
        grotto.Shortage.Should().Be(60);
        church.ConfirmedAvailable.Should().Be(0, "stock of another work area is never pooled (BR-24)");
        church.Shortage.Should().Be(10);
        grotto.Unit.Should().Be("m");
    }

    [Fact]
    public async Task Inventory_filters_by_work_area_and_shortage_and_hides_other_parishes()
    {
        _requirements.Add(MaterialRequirement.Create(_grotto.WorkAreaId, _cable.MaterialId, 10, 0, null, DateTimeOffset.UtcNow));
        await Opening().HandleAsync(new RecordOpeningStockCommand(_grotto.WorkAreaId, _cable.MaterialId, 40, null, null));
        _requirements.Add(MaterialRequirement.Create(_church.WorkAreaId, _cable.MaterialId, 5, 0, null, DateTimeOffset.UtcNow));

        (await List().HandleAsync(new InventoryFilter(1, 20, OnlyShortage: true))).Items.Should().ContainSingle()
            .Which.WorkAreaId.Should().Be(_church.WorkAreaId);
        (await List().HandleAsync(new InventoryFilter(1, 20, WorkAreaId: _grotto.WorkAreaId))).Items.Should().ContainSingle();

        var foreign = new ListInventoryQueryHandler(Access(Guid.NewGuid(), GrottoWorksRoles.Parish, Guid.NewGuid()), _scopes,
            new InventoryReader(_inventory, _catalog));
        (await foreign.HandleAsync(new InventoryFilter(1, 20))).TotalItems.Should().Be(0);
        (await Fails(() => foreign.HandleAsync(new InventoryFilter(1, 20, WorkAreaId: _grotto.WorkAreaId))))
            .Code.Should().Be("WORK_AREA_NOT_FOUND");
    }

    [Fact]
    public async Task Cancelled_requirements_do_not_count()
    {
        var requirement = MaterialRequirement.Create(_grotto.WorkAreaId, _cable.MaterialId, 10, 0, null, DateTimeOffset.UtcNow);
        requirement.Update(null, null, null, RequirementStatus.Cancelled, DateTimeOffset.UtcNow);
        _requirements.Add(requirement);

        (await List().HandleAsync(new InventoryFilter(1, 20))).TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task History_lists_movements_of_the_material_in_the_parish_newest_first()
    {
        await Opening().HandleAsync(new RecordOpeningStockCommand(_grotto.WorkAreaId, _cable.MaterialId, 40, DateTimeOffset.UtcNow.AddDays(-2), null));
        _inventory.Append(MaterialTransaction.Post(_grotto.WorkAreaId, _cable.MaterialId, TransactionType.TransferOut, 5,
            DateTimeOffset.UtcNow.AddDays(-1), null, null, TransactionReferenceType.SupportResponseMaterial, Guid.NewGuid()));

        var history = await new GetMaterialHistoryQueryHandler(Access(Guid.NewGuid(), GrottoWorksRoles.Leader), _scopes, _inventory, _catalog)
            .HandleAsync(new MaterialHistoryQuery(_cable.MaterialId, null, null, null));

        history.Select(row => row.Quantity).Should().Equal(-5, 40);

        (await Fails(() => new GetMaterialHistoryQueryHandler(Access(), _scopes, _inventory, _catalog)
                .HandleAsync(new MaterialHistoryQuery(_cable.MaterialId, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1)))))
            .Code.Should().Be("INVALID_DATE_RANGE");
    }
}
