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

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Manual_create_is_disabled()
    {
        var error = await Fails(() => new CreateMaterialRequirementCommandHandler().HandleAsync(
            new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 50.5m, null, "manual")));

        error.Code.Should().Be("MANUAL_MATERIAL_REQUIREMENT_DISABLED");
    }

    [Fact]
    public async Task Manual_update_is_disabled()
    {
        var error = await Fails(() => new UpdateMaterialRequirementCommandHandler().HandleAsync(
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

    [Fact]
    public async Task Derived_quantity_changes_without_changing_the_area_material_identity()
    {
        var otherArea = _scopes.Seed(Parish, _officer);
        var paint = _catalog.SeedMaterial(_catalog.SeedCategory(), "PAINT", "Sơn", "l");
        var cable = MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 8, 0, null, DateTimeOffset.UtcNow);
        _requirements.Add(cable);
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 12, 0, null, DateTimeOffset.UtcNow));
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, paint.MaterialId, 2, 0, null, DateTimeOffset.UtcNow));
        _requirements.Add(MaterialRequirement.Create(otherArea.WorkAreaId, _cable.MaterialId, 10, 0, null, DateTimeOffset.UtcNow));
        var store = new Resource.Infrastructure.Requirements.MaterialRequirementStore(new FakeInventoryStore(_requirements));
        var handler = new ListMaterialRequirementsQueryHandler(Access(), store, _catalog);

        var initial = await handler.HandleAsync(_area.WorkAreaId, null);
        var cableRow = initial.Single(row => row.MaterialId == _cable.MaterialId);
        cableRow.RequiredQuantity.Should().Be(20);
        initial.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        (await handler.HandleAsync(_area.WorkAreaId, null)).Should().Equal(initial);
        var other = (await handler.HandleAsync(otherArea.WorkAreaId, null)).Should().ContainSingle().Which;
        other.Id.Should().NotBe(cableRow.Id);

        cable.Update(10, null, null, null, DateTimeOffset.UtcNow);
        var updated = (await handler.HandleAsync(_area.WorkAreaId, null)).Single(row => row.MaterialId == _cable.MaterialId);
        updated.Id.Should().Be(cableRow.Id);
        updated.RequiredQuantity.Should().Be(22);
    }
}
