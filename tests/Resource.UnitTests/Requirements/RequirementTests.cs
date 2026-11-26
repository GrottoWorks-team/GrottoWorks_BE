using BuildingBlocks.Security;
using Resource.Application.Requirements;
using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Requirements;

/// <summary>F-RES-03: material requirements (unique per work area + material, quantity &gt; 0, MO of the area).</summary>
public sealed class RequirementTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly WorkAreaScope _area;
    private readonly Material _cable;

    public RequirementTests()
    {
        _area = _scopes.Seed(Parish, _officer);
        _cable = _catalog.SeedMaterial(_catalog.SeedCategory(), "CABLE", "Dây điện", "m");
    }

    private WorkAreaAccess Access(Guid? userId = null, string role = GrottoWorksRoles.MaterialOfficer, Guid? parish = null) =>
        new(_scopes, new FakeCurrentUser(userId ?? _officer, role, parish ?? Parish));

    private readonly FakeIntegrationEventPublisher _events = new();

    private Resource.Application.Shortages.ShortageTracker Tracker() =>
        new(new Resource.Application.Inventory.InventoryReader(new FakeInventoryStore(_requirements), _catalog), _events);

    private CreateMaterialRequirementCommandHandler Create(WorkAreaAccess? access = null) => new(access ?? Access(), _requirements, _catalog, Tracker(), new FakeUnitOfWork());

    private UpdateMaterialRequirementCommandHandler Update(WorkAreaAccess? access = null) => new(access ?? Access(), _requirements, _catalog, Tracker(), new FakeUnitOfWork());

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Manual_create_is_disabled()
    {
        var error = await Fails(() => Create().HandleAsync(
            new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 50.5m, null, "manual")));

        error.Code.Should().Be("MANUAL_MATERIAL_REQUIREMENT_DISABLED");
    }

    [Fact]
    public async Task Manual_update_is_disabled()
    {
        var error = await Fails(() => Update().HandleAsync(
            new UpdateMaterialRequirementCommand(Guid.NewGuid(), null, 25, null, null, RequirementStatus.Cancelled)));

        error.Code.Should().Be("MANUAL_MATERIAL_REQUIREMENT_DISABLED");
    }

    [Fact]
    public async Task List_is_readable_by_any_role_of_the_parish()
    {
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 10, 0, null, DateTimeOffset.UtcNow));
        var handler = new ListMaterialRequirementsQueryHandler(
            Access(Guid.NewGuid(), GrottoWorksRoles.Volunteer), _requirements, _catalog);

        (await handler.HandleAsync(_area.WorkAreaId, null)).Should().ContainSingle();
        (await handler.HandleAsync(_area.WorkAreaId, RequirementStatus.Active)).Should().ContainSingle();
    }
}
