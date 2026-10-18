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

    private CreateMaterialRequirementCommandHandler Create(WorkAreaAccess? access = null) => new(access ?? Access(), _requirements, _catalog);

    private UpdateMaterialRequirementCommandHandler Update(WorkAreaAccess? access = null) => new(access ?? Access(), _requirements, _catalog);

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Officer_creates_a_requirement_with_catalog_unit()
    {
        var dto = await Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 50.5m, null, " 2x1.5 "));

        dto.Unit.Should().Be("m");
        dto.MaterialCode.Should().Be("CABLE");
        dto.RequiredQuantity.Should().Be(50.5m);
        dto.ExistingQuantity.Should().Be(0);
        dto.Status.Should().Be(RequirementStatus.Active);
        dto.Note.Should().Be("2x1.5");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.005)]
    public async Task Quantity_must_be_positive_with_two_decimals(decimal quantity)
    {
        (await Fails(() => Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, quantity, null, null))))
            .Code.Should().Be("REQUIREMENT_QUANTITY_INVALID");
    }

    [Fact]
    public async Task One_requirement_per_work_area_and_material()
    {
        await Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 10, null, null));

        (await Fails(() => Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 5, null, null))))
            .Code.Should().Be("REQUIREMENT_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Only_the_assigned_officer_and_only_active_materials()
    {
        (await Fails(() => Create(Access(Guid.NewGuid())).HandleAsync(
                new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 10, null, null))))
            .Code.Should().Be("WORK_AREA_SCOPE_DENIED");

        _cable.Update(null, null, null, CatalogStatus.Inactive, false);
        (await Fails(() => Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 10, null, null))))
            .Code.Should().Be("MATERIAL_INACTIVE");
        (await Fails(() => Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, Guid.NewGuid(), 10, null, null))))
            .Code.Should().Be("MATERIAL_UNKNOWN");
    }

    [Fact]
    public async Task Archived_season_blocks_changes()
    {
        var archived = _scopes.Seed(Parish, _officer, seasonStatus: SeasonStatus.Archived);

        (await Fails(() => Create().HandleAsync(new CreateMaterialRequirementCommand(archived.WorkAreaId, _cable.MaterialId, 10, null, null))))
            .Code.Should().Be("SEASON_READ_ONLY");
    }

    [Fact]
    public async Task Update_changes_quantity_cancels_and_checks_version()
    {
        var created = await Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 10, null, "x"));

        var updated = await Update().HandleAsync(new UpdateMaterialRequirementCommand(created.Id, created.Version, 25, 3, "", RequirementStatus.Cancelled));
        updated.RequiredQuantity.Should().Be(25);
        updated.ExistingQuantity.Should().Be(3);
        updated.Note.Should().BeNull();
        updated.Status.Should().Be(RequirementStatus.Cancelled);

        var stale = await Fails(() => Update().HandleAsync(new UpdateMaterialRequirementCommand(created.Id, created.Version + 9, 30, null, null, null)));
        stale.Code.Should().Be("REQUIREMENT_VERSION_MISMATCH");
        stale.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Update_by_officer_of_another_parish_looks_like_not_found()
    {
        var created = await Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 10, null, null));

        (await Fails(() => Update(Access(Guid.NewGuid(), parish: Guid.NewGuid())).HandleAsync(
                new UpdateMaterialRequirementCommand(created.Id, null, 1, null, null, null))))
            .Code.Should().Be("WORK_AREA_NOT_FOUND");
    }

    [Fact]
    public async Task List_is_readable_by_any_role_of_the_parish_and_filters_status()
    {
        await Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 10, null, null));
        var paint = _catalog.SeedMaterial(_catalog.Categories[0], "PAINT", "Sơn", "lít");
        var second = await Create().HandleAsync(new CreateMaterialRequirementCommand(_area.WorkAreaId, paint.MaterialId, 4, null, null));
        await Update().HandleAsync(new UpdateMaterialRequirementCommand(second.Id, null, null, null, null, RequirementStatus.Cancelled));

        var handler = new ListMaterialRequirementsQueryHandler(Access(Guid.NewGuid(), GrottoWorksRoles.Volunteer), _requirements, _catalog);

        (await handler.HandleAsync(_area.WorkAreaId, null)).Should().HaveCount(2);
        (await handler.HandleAsync(_area.WorkAreaId, RequirementStatus.Active)).Select(r => r.MaterialCode).Should().Equal("CABLE");
    }
}
