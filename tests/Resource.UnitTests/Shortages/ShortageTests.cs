using BuildingBlocks.Security;
using Resource.Application.Inventory;
using Resource.Application.Requirements;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Shortages;

/// <summary>F-RES-05: shortages per work area, season material needs, material.shortage.changed.</summary>
public sealed class ShortageTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly Guid _season = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _grotto;
    private readonly WorkAreaScope _church;
    private readonly Material _cable;
    private readonly Material _paint;

    public ShortageTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _grotto = _scopes.Seed(Parish, _officer, _season, "Hang đá");
        _church = _scopes.Seed(Parish, _officer, _season, "Nhà thờ");
        var category = _catalog.SeedCategory();
        _cable = _catalog.SeedMaterial(category, "CABLE", "Dây điện", "m");
        _paint = _catalog.SeedMaterial(category, "PAINT", "Sơn", "lít");
    }

    private WorkAreaAccess Access(Guid? user = null, string role = GrottoWorksRoles.MaterialOfficer, Guid? parish = null) =>
        new(_scopes, new FakeCurrentUser(user ?? _officer, role, parish ?? Parish));

    private InventoryReader Reader() => new(_inventory, _catalog);

    private ShortageTracker Tracker() => new(Reader(), _events);

    private Task<MaterialRequirementDto> Require(WorkAreaScope area, Material material, decimal quantity) =>
        new CreateMaterialRequirementCommandHandler(Access(), _requirements, _catalog, Tracker())
            .HandleAsync(new CreateMaterialRequirementCommand(area.WorkAreaId, material.MaterialId, quantity, null, null));

    private Task Open(WorkAreaScope area, Material material, decimal quantity) =>
        new RecordOpeningStockCommandHandler(Access(), new FakeCurrentUser(_officer, GrottoWorksRoles.MaterialOfficer, Parish),
                _inventory, _catalog, Tracker())
            .HandleAsync(new RecordOpeningStockCommand(area.WorkAreaId, material.MaterialId, quantity, null, null));

    [Fact]
    public async Task Work_area_shortages_list_every_required_line_biggest_first()
    {
        await Require(_grotto, _cable, 100);
        await Require(_grotto, _paint, 5);
        await Open(_grotto, _cable, 40);
        await Open(_grotto, _paint, 9);

        var rows = await new GetWorkAreaShortagesQueryHandler(Access(Guid.NewGuid(), GrottoWorksRoles.Leader), Reader())
            .HandleAsync(_grotto.WorkAreaId);

        rows.Select(row => (row.MaterialCode, row.Shortage)).Should().Equal(("CABLE", 60m), ("PAINT", 0m));
        rows[1].ConfirmedAvailable.Should().Be(9, "surplus stays visible but shortage is never negative (BR-25)");
    }

    [Fact]
    public async Task Stock_without_requirement_is_not_a_shortage_line()
    {
        await Open(_grotto, _cable, 10);

        (await new GetWorkAreaShortagesQueryHandler(Access(), Reader()).HandleAsync(_grotto.WorkAreaId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Material_needs_show_only_positive_shortages_of_the_season_and_parish()
    {
        await Require(_grotto, _cable, 100);
        await Require(_church, _cable, 10);
        await Open(_church, _cable, 10);
        var otherSeason = _scopes.Seed(Parish, _officer, Guid.NewGuid(), "Khác");
        await Require(otherSeason, _paint, 3);

        var volunteer = new ListMaterialNeedsQueryHandler(Access(Guid.NewGuid(), GrottoWorksRoles.Volunteer), _scopes, Reader());
        var needs = await volunteer.HandleAsync(new MaterialNeedFilter(1, 20, _season));

        needs.Items.Should().ContainSingle();
        needs.Items[0].WorkAreaName.Should().Be("Hang đá");
        needs.Items[0].Shortage.Should().Be(100);

        var foreign = new ListMaterialNeedsQueryHandler(Access(Guid.NewGuid(), GrottoWorksRoles.Volunteer, Guid.NewGuid()), _scopes, Reader());
        (await foreign.HandleAsync(new MaterialNeedFilter(1, 20, _season))).TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Shortage_changed_is_published_on_requirement_and_stock_changes_only_when_it_moves()
    {
        var requirement = await Require(_grotto, _cable, 100);
        await Open(_grotto, _cable, 40);
        await new UpdateMaterialRequirementCommandHandler(Access(), _requirements, _catalog, Tracker())
            .HandleAsync(new UpdateMaterialRequirementCommand(requirement.Id, null, null, null, "chỉ sửa ghi chú", null));
        await new UpdateMaterialRequirementCommandHandler(Access(), _requirements, _catalog, Tracker())
            .HandleAsync(new UpdateMaterialRequirementCommand(requirement.Id, null, null, null, null, RequirementStatus.Cancelled));

        var payloads = _events.Events.Select(e => (MaterialShortageChanged)e.Payload).ToList();
        _events.Events.Should().OnlyContain(e => e.EventType == MaterialShortageChanged.EventType);
        payloads.Select(p => (p.PreviousShortage, p.Shortage)).Should().Equal((0m, 100m), (100m, 60m), (60m, 0m));
        payloads[1].ConfirmedAvailable.Should().Be(40);
        payloads.Should().OnlyContain(p => p.WorkAreaId == _grotto.WorkAreaId && p.SeasonId == _season && p.ParishId == Parish);
        payloads.Should().OnlyContain(p => p.MaterialCode == _cable.Code && p.MaterialName == _cable.Name && p.Unit == _cable.Unit);
    }

    [Fact]
    public async Task Shortages_of_another_parish_are_hidden()
    {
        var act = () => new GetWorkAreaShortagesQueryHandler(Access(Guid.NewGuid(), GrottoWorksRoles.Parish, Guid.NewGuid()), Reader())
            .HandleAsync(_grotto.WorkAreaId);

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("WORK_AREA_NOT_FOUND");
    }
}
