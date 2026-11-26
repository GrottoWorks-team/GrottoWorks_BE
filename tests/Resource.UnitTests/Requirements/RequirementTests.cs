using BuildingBlocks.Security;
using Resource.Application.Requirements;
using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Requirements;

/// <summary>
/// F-RES-03: the material estimate of a work area (one line per material, quantity &gt; 0). The material officer owns
/// it; the parish committee may set it while the season is still PLANNING (26/11/2026) — after activation it is frozen.
/// </summary>
public sealed class RequirementTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly Guid _parishUser = Guid.NewGuid();
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

    private static FakeCurrentUser User(Guid userId, string role) => new(userId, role, Parish);

    private WorkAreaAccess Access(Guid? userId = null, string role = GrottoWorksRoles.MaterialOfficer) =>
        new(_scopes, User(userId ?? _officer, role));

    private readonly FakeIntegrationEventPublisher _events = new();

    private Resource.Application.Shortages.ShortageTracker Tracker() =>
        new(new Resource.Application.Inventory.InventoryReader(new FakeInventoryStore(_requirements), _catalog), _events);

    private CreateMaterialRequirementCommandHandler Create(Guid? userId = null, string role = GrottoWorksRoles.MaterialOfficer) =>
        new(Access(userId, role), _requirements, _catalog, Tracker(), User(userId ?? _officer, role), new FakeUnitOfWork());

    private UpdateMaterialRequirementCommandHandler Update(Guid? userId = null, string role = GrottoWorksRoles.MaterialOfficer) =>
        new(Access(userId, role), _requirements, _catalog, Tracker(), User(userId ?? _officer, role), new FakeUnitOfWork());

    private MaterialRequirement SeedEstimate(decimal quantity = 10) =>
        MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, quantity, 0, null, DateTimeOffset.UtcNow);

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Officer_adds_an_estimate_line_for_the_area()
    {
        var dto = await Create().HandleAsync(
            new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 50.5m, null, "ước lượng ban đầu"));

        dto.RequiredQuantity.Should().Be(50.5m);
        dto.Status.Should().Be(RequirementStatus.Active);
        dto.MaterialCode.Should().Be("CABLE");
        _requirements.Requirements.Should().ContainSingle();
    }

    [Fact]
    public async Task Only_the_officer_of_the_area_or_the_parish_while_planning_can_write()
    {
        // another material officer of the same parish
        (await Fails(() => Create(Guid.NewGuid()).HandleAsync(
            new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 5, null, null)))).Code
            .Should().Be("WORK_AREA_SCOPE_DENIED");

        // the parish cannot change the estimate once the season is running (fixture season is ACTIVE)
        (await Fails(() => Create(_parishUser, GrottoWorksRoles.Parish).HandleAsync(
            new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 5, null, null)))).Code
            .Should().Be("WORK_AREA_SCOPE_DENIED");
    }

    [Fact]
    public async Task Parish_can_set_the_estimate_while_the_season_is_still_being_planned()
    {
        var planning = _scopes.Seed(Parish, _officer, null, "Hang đá", SeasonStatus.Planning);

        var dto = await Create(_parishUser, GrottoWorksRoles.Parish).HandleAsync(
            new CreateMaterialRequirementCommand(planning.WorkAreaId, _cable.MaterialId, 20, null, null));

        dto.RequiredQuantity.Should().Be(20);
    }

    [Fact]
    public async Task Unknown_or_duplicate_material_is_rejected()
    {
        (await Fails(() => Create().HandleAsync(
            new CreateMaterialRequirementCommand(_area.WorkAreaId, Guid.NewGuid(), 5, null, null)))).Code
            .Should().Be("MATERIAL_UNKNOWN");

        _requirements.Add(SeedEstimate());
        (await Fails(() => Create().HandleAsync(
            new CreateMaterialRequirementCommand(_area.WorkAreaId, _cable.MaterialId, 5, null, null)))).Code
            .Should().Be("REQUIREMENT_EXISTS");
    }

    [Fact]
    public async Task Update_changes_quantity_and_can_cancel_the_line()
    {
        var requirement = SeedEstimate(10);
        _requirements.Add(requirement);

        var updated = await Update().HandleAsync(
            new UpdateMaterialRequirementCommand(requirement.MaterialRequirementId, null, 25, null, "tăng", null));
        updated.RequiredQuantity.Should().Be(25);

        var cancelled = await Update().HandleAsync(
            new UpdateMaterialRequirementCommand(requirement.MaterialRequirementId, null, null, null, null, RequirementStatus.Cancelled));
        cancelled.Status.Should().Be(RequirementStatus.Cancelled);
    }

    [Fact]
    public async Task Update_requires_an_existing_line_and_the_right_version()
    {
        (await Fails(() => Update().HandleAsync(
            new UpdateMaterialRequirementCommand(Guid.NewGuid(), null, 1, null, null, null)))).Code
            .Should().Be("REQUIREMENT_NOT_FOUND");

        var requirement = SeedEstimate(10);
        _requirements.Add(requirement);
        (await Fails(() => Update().HandleAsync(
            new UpdateMaterialRequirementCommand(requirement.MaterialRequirementId, 99, 15, null, null, null)))).Code
            .Should().Be("REQUIREMENT_VERSION_MISMATCH");
    }

    [Fact]
    public async Task List_is_readable_by_any_role_of_the_parish()
    {
        _requirements.Add(SeedEstimate(10));
        var handler = new ListMaterialRequirementsQueryHandler(
            Access(Guid.NewGuid(), GrottoWorksRoles.Volunteer), _requirements, _catalog);

        (await handler.HandleAsync(_area.WorkAreaId, null)).Should().ContainSingle();
        (await handler.HandleAsync(_area.WorkAreaId, RequirementStatus.Active)).Should().ContainSingle();
    }
}
