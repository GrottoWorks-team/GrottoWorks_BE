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

/// <summary>F-RES-13: direct donations — RECEIVED + first receipt, usable-only stock posting, events, read scopes.</summary>
public sealed class DonationTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakeDonorStore _donors = new();
    private readonly FakeDonationStore _store = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _area;
    private readonly Material _cable;
    private readonly Material _bulb;
    private readonly Donor _external;
    private readonly Donor _volunteerDonor;
    private readonly Guid _volunteer = Guid.NewGuid();

    public DonationTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        var category = _catalog.SeedCategory();
        _cable = _catalog.SeedMaterial(category, "CABLE", "Dây điện", "m");
        _bulb = _catalog.SeedMaterial(category, "BULB", "Bóng đèn", "cái");
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 50, 0, null, DateTimeOffset.UtcNow));
        _external = Donor.Create(Parish, null, "Bà Maria Lan", null, null, null, null, _officer, DateTimeOffset.UtcNow);
        _volunteerDonor = Donor.Create(Parish, _volunteer, "Anh Phêrô Minh", null, null, null, null, _officer, DateTimeOffset.UtcNow);
        _donors.Add(_external);
        _donors.Add(_volunteerDonor);
    }

    private FakeCurrentUser Officer(Guid? id = null, Guid? parish = null) => new(id ?? _officer, GrottoWorksRoles.MaterialOfficer, parish ?? Parish);

    private DonationProjector Projector() => new(_donors, _catalog);

    private RecordDirectDonationCommandHandler Record(FakeCurrentUser? user = null)
    {
        var current = user ?? Officer();
        return new(new DonationOfficerScope(new WorkAreaAccess(_scopes, current), _scopes, current), _donors, _store, _catalog,
            new DonationReceiptPoster(_inventory, new ShortageTracker(new InventoryReader(_inventory, _catalog), _events), _events, current),
            Projector(), current);
    }

    private GetDonationQueryHandler Get(FakeCurrentUser user) =>
        new(new WorkAreaAccess(_scopes, user), _scopes, _store, _donors, Projector(), user);

    private RecordDirectDonationCommand Material(params ReceivedMaterialInput[] items) =>
        new(_external.DonorId, DonationType.Material, null, _area.WorkAreaId, null, items, null, "Giao tận nơi");

    private RecordDirectDonationCommand Money(decimal? amount, Guid? workAreaId = null, Guid? seasonId = null, Guid? donorId = null) =>
        new(donorId ?? _external.DonorId, DonationType.Money, seasonId, workAreaId, amount, [], null, null);

    private ReceivedMaterialInput Cable(decimal received, decimal unusable = 0) => new(_cable.MaterialId, received, unusable, null);

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Material_donation_posts_only_the_usable_quantity_to_the_receiving_work_area()
    {
        // BA 8.7 example: 20 m received, 2 unusable → 18 to stock; 10 bulbs, 1 unusable → 9.
        var dto = await Record().HandleAsync(Material(Cable(20, 2), new ReceivedMaterialInput(_bulb.MaterialId, 10, 1, null)));

        dto.Status.Should().Be(DonationStatus.Received);
        dto.Type.Should().Be(DonationType.Material);
        dto.DonorName.Should().Be("Bà Maria Lan");
        dto.SeasonId.Should().Be(_area.SeasonId);
        dto.ReceivedAmount.Should().BeNull();
        var cable = dto.Items.Single(item => item.MaterialId == _cable.MaterialId);
        cable.PledgedQuantity.Should().BeNull();
        cable.ReceivedQuantity.Should().Be(20);
        cable.UnusableQuantity.Should().Be(2);
        cable.UsableQuantity.Should().Be(18);
        cable.Status.Should().Be(DonationItemStatus.FullyReceived);
        dto.Receipts.Should().ContainSingle().Which.Items.Should().HaveCount(2);

        _inventory.Ledger.Should().HaveCount(2);
        var posted = _inventory.Ledger.Single(t => t.MaterialId == _cable.MaterialId);
        posted.Type.Should().Be(TransactionType.DonationReceipt);
        posted.Quantity.Should().Be(18);
        posted.WorkAreaId.Should().Be(_area.WorkAreaId);
        posted.ReferenceType.Should().Be(TransactionReferenceType.DonationReceiptItem);
        posted.ReferenceId.Should().Be(dto.Receipts![0].Items.Single(line => line.MaterialId == _cable.MaterialId).Id);
    }

    [Fact]
    public async Task Fully_unusable_line_posts_nothing_and_is_marked_unusable()
    {
        var dto = await Record().HandleAsync(Material(Cable(5, 5)));

        _inventory.Ledger.Should().BeEmpty();
        dto.Items.Single().Status.Should().Be(DonationItemStatus.Unusable);
        _events.Events.Should().NotContain(e => e.EventType == MaterialShortageChanged.EventType);
    }

    [Fact]
    public async Task Receipt_publishes_contribution_and_shortage_events()
    {
        var dto = await Record().HandleAsync(Material(Cable(20, 2)));

        var shortage = _events.Events.Single(e => e.EventType == MaterialShortageChanged.EventType).Payload.Should().BeOfType<MaterialShortageChanged>().Which;
        shortage.PreviousShortage.Should().Be(50);
        shortage.Shortage.Should().Be(32);

        var receipt = _events.Events.Single(e => e.EventType == ContributionReceiptConfirmed.EventType);
        receipt.AggregateId.Should().Be(dto.Id);
        var payload = receipt.Payload.Should().BeOfType<ContributionReceiptConfirmed>().Which;
        payload.IsDirect.Should().BeTrue();
        payload.IsFirstReceipt.Should().BeTrue();
        payload.DonorUserId.Should().BeNull();
        payload.ParishId.Should().Be(Parish);
        payload.DonationType.Should().Be("MATERIAL");
        payload.Items.Should().ContainSingle(line => line.MaterialId == _cable.MaterialId && line.UsableQuantity == 18);
    }

    [Fact]
    public async Task Money_donation_records_the_amount_and_never_touches_stock()
    {
        var dto = await Record().HandleAsync(Money(2_000_000m, seasonId: _area.SeasonId, donorId: _volunteerDonor.DonorId));

        dto.ReceivedAmount.Should().Be(new Application.Purchases.MoneyDto(2_000_000m, "VND"));
        dto.WorkAreaId.Should().BeNull();
        dto.Items.Should().BeEmpty();
        dto.Receipts.Should().ContainSingle().Which.ReceivedAmount!.Amount.Should().Be(2_000_000m);
        _inventory.Ledger.Should().BeEmpty();
        var payload = _events.Events.Single().Payload.Should().BeOfType<ContributionReceiptConfirmed>().Which;
        payload.DonationType.Should().Be("MONEY");
        payload.ReceivedAmount.Should().Be(2_000_000m);
        payload.DonorUserId.Should().Be(_volunteer);
    }

    [Theory]
    [InlineData(null, "DONATION_AMOUNT_REQUIRED")]
    [InlineData(0.0, "DONATION_AMOUNT_INVALID")]
    [InlineData(-5.0, "DONATION_AMOUNT_INVALID")]
    [InlineData(10.005, "DONATION_AMOUNT_INVALID")]
    public async Task Money_amount_must_be_positive(double? amount, string code)
    {
        (await Fails(() => Record().HandleAsync(Money((decimal?)amount, workAreaId: _area.WorkAreaId)))).Code.Should().Be(code);
    }

    [Fact]
    public async Task Type_rules_follow_BR_36_and_BR_37()
    {
        (await Fails(() => Record().HandleAsync(Money(100, _area.WorkAreaId) with { Items = [Cable(1)] })))
            .Code.Should().Be("DONATION_MONEY_HAS_ITEMS");
        (await Fails(() => Record().HandleAsync(Material(Cable(1)) with { Amount = 100 }))).Code.Should().Be("DONATION_MATERIAL_HAS_AMOUNT");
        (await Fails(() => Record().HandleAsync(Material())))
            .Code.Should().Be("DONATION_ITEMS_REQUIRED");
        (await Fails(() => Record().HandleAsync(Material(Cable(1)) with { WorkAreaId = null, SeasonId = _area.SeasonId })))
            .Code.Should().Be("DONATION_WORK_AREA_REQUIRED");
        (await Fails(() => Record().HandleAsync(Money(100))))
            .Code.Should().Be("DONATION_SEASON_REQUIRED");
    }

    [Theory]
    [InlineData(0, 0, "DONATION_RECEIVED_QUANTITY_INVALID")]
    [InlineData(5, 6, "DONATION_UNUSABLE_QUANTITY_INVALID")]
    [InlineData(5, -1, "DONATION_UNUSABLE_QUANTITY_INVALID")]
    public async Task Received_and_unusable_quantities_are_validated(decimal received, decimal unusable, string code)
    {
        (await Fails(() => Record().HandleAsync(Material(Cable(received, unusable))))).Code.Should().Be(code);
        _inventory.Ledger.Should().BeEmpty();
        _store.Donations.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_or_inactive_materials_and_future_receipts_are_rejected()
    {
        (await Fails(() => Record().HandleAsync(Material(Cable(1), Cable(2))))).Code.Should().Be("DONATION_DUPLICATE_MATERIAL");
        (await Fails(() => Record().HandleAsync(Material(Cable(1)) with { ReceivedAt = DateTimeOffset.UtcNow.AddHours(1) })))
            .Code.Should().Be("DONATION_RECEIVED_IN_FUTURE");

        _cable.Update(null, null, null, CatalogStatus.Inactive, isReferenced: true);
        (await Fails(() => Record().HandleAsync(Material(Cable(1))))).Code.Should().Be("MATERIAL_INACTIVE");
    }

    [Fact]
    public async Task Donor_must_belong_to_the_parish()
    {
        var foreign = Donor.Create(Guid.NewGuid(), null, "Người ngoài", null, null, null, null, _officer, DateTimeOffset.UtcNow);
        _donors.Add(foreign);

        (await Fails(() => Record().HandleAsync(Material(Cable(1)) with { DonorId = foreign.DonorId }))).Code.Should().Be("DONOR_UNKNOWN");
        (await Fails(() => Record().HandleAsync(Material(Cable(1)) with { DonorId = Guid.NewGuid() }))).Code.Should().Be("DONOR_UNKNOWN");
    }

    [Fact]
    public async Task Only_the_material_officer_of_the_work_area_or_season_records()
    {
        (await Fails(() => Record(Officer(Guid.NewGuid())).HandleAsync(Material(Cable(1))))).Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        (await Fails(() => Record(Officer(Guid.NewGuid())).HandleAsync(Money(100, seasonId: _area.SeasonId))))
            .Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        (await Fails(() => Record().HandleAsync(Money(100, seasonId: Guid.NewGuid())))).Code.Should().Be("SEASON_NOT_FOUND");
        (await Fails(() => Record().HandleAsync(Material(Cable(1)) with { SeasonId = Guid.NewGuid() }))).Code.Should().Be("DONATION_SEASON_MISMATCH");

        var archived = _scopes.Seed(Parish, _officer, seasonStatus: SeasonStatus.Archived);
        (await Fails(() => Record().HandleAsync(Material(Cable(1)) with { WorkAreaId = archived.WorkAreaId }))).Code.Should().Be("SEASON_READ_ONLY");
        (await Fails(() => Record().HandleAsync(Money(100, seasonId: archived.SeasonId)))).Code.Should().Be("SEASON_READ_ONLY");
    }

    [Fact]
    public async Task Volunteer_sees_only_donations_of_their_linked_donor()
    {
        var mine = await Record().HandleAsync(Money(500_000m, _area.WorkAreaId, donorId: _volunteerDonor.DonorId));
        var other = await Record().HandleAsync(Material(Cable(3)));

        var volunteer = new FakeCurrentUser(_volunteer, GrottoWorksRoles.Volunteer, Parish);
        (await Get(volunteer).HandleAsync(mine.Id)).Id.Should().Be(mine.Id);
        (await Fails(() => Get(volunteer).HandleAsync(other.Id))).Code.Should().Be("DONATION_NOT_FOUND");

        var contributions = await new ListMyContributionsQueryHandler(_donors, _store, Projector(), volunteer).HandleAsync(1, 20, null);
        contributions.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);

        var stranger = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Volunteer, Parish);
        (await new ListMyContributionsQueryHandler(_donors, _store, Projector(), stranger).HandleAsync(1, 20, null)).TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Officers_read_donations_of_their_parish_only()
    {
        var dto = await Record().HandleAsync(Material(Cable(3)));

        (await Get(Officer(Guid.NewGuid())).HandleAsync(dto.Id)).Receipts.Should().ContainSingle();
        (await Fails(() => Get(Officer(Guid.NewGuid(), Guid.NewGuid())).HandleAsync(dto.Id))).Code.Should().Be("DONATION_NOT_FOUND");

        var leader = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Leader, Parish);
        var list = new ListDonationsQueryHandler(new WorkAreaAccess(_scopes, leader), _scopes, _store, Projector());
        var page = await list.HandleAsync(new DonationFilter(1, 20, null, null, null, DonationType.Material, null));
        page.Items.Should().ContainSingle().Which.Receipts.Should().BeNull();
        (await list.HandleAsync(new DonationFilter(1, 20, null, null, null, DonationType.Money, null))).TotalItems.Should().Be(0);

        var outsider = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Parish, Guid.NewGuid());
        (await new ListDonationsQueryHandler(new WorkAreaAccess(_scopes, outsider), _scopes, _store, Projector())
            .HandleAsync(new DonationFilter(1, 20, null, null, null, null, null))).TotalItems.Should().Be(0);
    }
}
