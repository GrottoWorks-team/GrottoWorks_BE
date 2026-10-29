using BuildingBlocks.Security;
using Resource.Application.Contributions;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Contributions;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Contributions;

/// <summary>F-RES-13b: pledges — volunteer donor link, never stock (BR-39), contribution needs, cancel with reason.</summary>
public sealed class PledgeTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly Guid _volunteerId = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakeDonorStore _donors = new();
    private readonly FakeDonationStore _store = new();
    private readonly FakeIdentityUserLookup _identity = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _area;
    private readonly Material _cable;
    private readonly Material _paint;

    public PledgeTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        var category = _catalog.SeedCategory();
        _cable = _catalog.SeedMaterial(category, "CABLE", "Dây điện", "m");
        _paint = _catalog.SeedMaterial(category, "PAINT", "Sơn", "lít");
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 50, 0, null, DateTimeOffset.UtcNow));
        _inventory.Append(MaterialTransaction.Post(_area.WorkAreaId, _cable.MaterialId, TransactionType.OpeningStock, 20, DateTimeOffset.UtcNow, _officer, null));
        _identity.Users[_volunteerId] = new Resource.Application.Users.IdentityUser(_volunteerId, "Phêrô Minh", "ACTIVE", "VOLUNTEER", Parish);
    }

    private FakeCurrentUser Volunteer(Guid? id = null, Guid? parish = null) => new(id ?? _volunteerId, GrottoWorksRoles.Volunteer, parish ?? Parish);

    private FakeCurrentUser Officer(Guid? id = null) => new(id ?? _officer, GrottoWorksRoles.MaterialOfficer, Parish);

    private DonationProjector Projector() => new(_donors, _catalog);

    private PledgeMaterialCommandHandler PledgeMaterial(FakeCurrentUser? user = null)
    {
        var current = user ?? Volunteer();
        return new(new WorkAreaAccess(_scopes, current), _requirements, _catalog, new VolunteerDonorResolver(_donors, _identity, current),
            _store, Projector(), _events, current);
    }

    private PledgeMoneyCommandHandler PledgeMoney(FakeCurrentUser? user = null)
    {
        var current = user ?? Volunteer();
        return new(new WorkAreaAccess(_scopes, current), _scopes, new VolunteerDonorResolver(_donors, _identity, current), _store, Projector(), _events, current);
    }

    private CancelPledgeCommandHandler Cancel(FakeCurrentUser user) =>
        new(new MaterialOfficerScope(new WorkAreaAccess(_scopes, user), _scopes, user), _store, _donors, Projector(), user);

    private ListContributionNeedsQueryHandler Needs(FakeCurrentUser? user = null)
    {
        var current = user ?? Volunteer();
        return new(new WorkAreaAccess(_scopes, current), _scopes, new InventoryReader(_inventory, _catalog), _store);
    }

    private PledgeMaterialCommand Cable(decimal quantity) => new(_area.WorkAreaId, [new PledgedMaterialInput(_cable.MaterialId, quantity, null)], "Tuần sau mang tới");

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task First_pledge_creates_the_volunteer_donor_and_later_ones_reuse_it()
    {
        var first = await PledgeMaterial().HandleAsync(Cable(10));
        var second = await PledgeMoney().HandleAsync(new PledgeMoneyCommand(_area.SeasonId, null, 500_000m, null));

        _donors.Donors.Should().ContainSingle();
        var donor = _donors.Donors.Single();
        donor.UserId.Should().Be(_volunteerId);
        donor.DonorName.Should().Be("Phêrô Minh");
        donor.ParishId.Should().Be(Parish);
        first.DonorId.Should().Be(donor.DonorId);
        second.DonorId.Should().Be(donor.DonorId);
    }

    [Fact]
    public async Task Material_pledge_never_touches_stock()
    {
        var dto = await PledgeMaterial().HandleAsync(Cable(10));

        dto.Status.Should().Be(DonationStatus.Pledged);
        dto.Items.Single().PledgedQuantity.Should().Be(10);
        dto.Items.Single().ReceivedQuantity.Should().Be(0);
        dto.Items.Single().Status.Should().Be(DonationItemStatus.Pledged);
        dto.Receipts.Should().BeEmpty();
        _inventory.Ledger.Should().ContainSingle(t => t.Type == TransactionType.OpeningStock);
        _events.Events.Should().NotContain(e => e.EventType == MaterialShortageChanged.EventType);

        var payload = _events.Events.Single().Payload.Should().BeOfType<ContributionPledged>().Which;
        payload.DonationType.Should().Be("MATERIAL");
        payload.MaterialOfficerUserId.Should().Be(_officer);
        payload.DonorUserId.Should().Be(_volunteerId);
        payload.Items.Should().ContainSingle(line => line.PledgedQuantity == 10);
    }

    [Fact]
    public async Task Money_pledge_keeps_the_amount_as_pledged_not_received()
    {
        var dto = await PledgeMoney().HandleAsync(new PledgeMoneyCommand(_area.SeasonId, _area.WorkAreaId, 1_000_000m, null));

        dto.PledgedAmount!.Amount.Should().Be(1_000_000m);
        dto.ReceivedAmount.Should().BeNull();
        dto.Status.Should().Be(DonationStatus.Pledged);
    }

    [Fact]
    public async Task Pledges_are_for_listed_needs_in_the_own_parish()
    {
        (await Fails(() => PledgeMaterial().HandleAsync(new PledgeMaterialCommand(_area.WorkAreaId,
            [new PledgedMaterialInput(_paint.MaterialId, 1, null)], null)))).Code.Should().Be("PLEDGE_MATERIAL_NOT_REQUIRED");
        (await Fails(() => PledgeMaterial().HandleAsync(Cable(0)))).Code.Should().Be("DONATION_PLEDGED_QUANTITY_INVALID");
        (await Fails(() => PledgeMaterial(Volunteer(parish: Guid.NewGuid())).HandleAsync(Cable(1)))).Code.Should().Be("WORK_AREA_NOT_FOUND");
        (await Fails(() => PledgeMoney().HandleAsync(new PledgeMoneyCommand(Guid.NewGuid(), null, 10, null)))).Code.Should().Be("SEASON_NOT_FOUND");
        (await Fails(() => PledgeMoney().HandleAsync(new PledgeMoneyCommand(_area.SeasonId, null, 0, null)))).Code.Should().Be("DONATION_AMOUNT_INVALID");

        var archived = _scopes.Seed(Parish, _officer, seasonStatus: SeasonStatus.Archived);
        (await Fails(() => PledgeMoney().HandleAsync(new PledgeMoneyCommand(archived.SeasonId, null, 10, null)))).Code.Should().Be("SEASON_READ_ONLY");
        _store.Donations.Should().BeEmpty();
        _store.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Identity_down_still_accepts_the_pledge_with_a_placeholder_name()
    {
        _identity.Unavailable = true;

        await PledgeMaterial().HandleAsync(Cable(1));

        _donors.Donors.Single().DonorName.Should().Be("Tình nguyện viên");
    }

    [Fact]
    public async Task Contribution_needs_subtract_outstanding_pledges()
    {
        // Required 50, stock 20 → shortage 30; 12 already pledged → 18 still needed.
        await PledgeMaterial().HandleAsync(Cable(12));

        var need = (await Needs().HandleAsync(new MaterialNeedFilter(1, 20, _area.SeasonId))).Items.Should().ContainSingle().Which;

        need.Kind.Should().Be("MATERIAL");
        need.Shortage.Should().Be(30);
        need.PledgedOutstanding.Should().Be(12);
        need.RemainingNeed.Should().Be(18);
    }

    [Fact]
    public async Task Owner_or_officer_cancels_the_outstanding_pledge_with_a_reason()
    {
        var pledge = await PledgeMaterial().HandleAsync(Cable(12));

        (await Fails(() => Cancel(Volunteer()).HandleAsync(new CancelPledgeCommand(pledge.Id, null, " ")))).Code.Should().Be("DONATION_CANCEL_REASON_REQUIRED");
        (await Fails(() => Cancel(Volunteer(Guid.NewGuid())).HandleAsync(new CancelPledgeCommand(pledge.Id, null, "x")))).Code.Should().Be("DONATION_NOT_FOUND");
        (await Fails(() => Cancel(Officer(Guid.NewGuid())).HandleAsync(new CancelPledgeCommand(pledge.Id, null, "x")))).Code.Should().Be("WORK_AREA_SCOPE_DENIED");

        var cancelled = await Cancel(Officer()).HandleAsync(new CancelPledgeCommand(pledge.Id, pledge.Version, "Người tặng đổi ý"));

        cancelled.Status.Should().Be(DonationStatus.Cancelled);
        cancelled.CancelReason.Should().Be("Người tặng đổi ý");
        cancelled.Items.Single().Status.Should().Be(DonationItemStatus.Cancelled);
        (await Needs().HandleAsync(new MaterialNeedFilter(1, 20, _area.SeasonId))).Items.Single().RemainingNeed.Should().Be(30);
        (await Fails(() => Cancel(Volunteer()).HandleAsync(new CancelPledgeCommand(pledge.Id, null, "lần nữa")))).Code.Should().Be("DONATION_INVALID_STATE");
    }

    [Fact]
    public async Task Direct_donations_cannot_be_cancelled()
    {
        var donor = Donor.Create(Parish, null, "Bà Lan", null, null, null, null, _officer, DateTimeOffset.UtcNow);
        _donors.Add(donor);
        var direct = Donation.RecordDirect(donor.DonorId, _area.SeasonId, _area.WorkAreaId, DonationType.Material, null,
            [new ReceivedMaterialInput(_cable.MaterialId, 1, 0, null)], DateTimeOffset.UtcNow, null, _officer, DateTimeOffset.UtcNow);
        _store.Add(direct);

        (await Fails(() => Cancel(Officer()).HandleAsync(new CancelPledgeCommand(direct.DonationId, null, "x")))).Code.Should().Be("DONATION_INVALID_STATE");
    }
}
