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

/// <summary>F-RES-13c: receipts of a pledge — cumulated totals, status, usable-only posting, over-receipt warning.</summary>
public sealed class DonationReceiptTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly Guid _volunteer = Guid.NewGuid();
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
    private readonly Donor _donor;

    public DonationReceiptTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        var category = _catalog.SeedCategory();
        _cable = _catalog.SeedMaterial(category, "CABLE", "Dây điện", "m");
        _bulb = _catalog.SeedMaterial(category, "BULB", "Bóng đèn", "cái");
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 50, 0, null, DateTimeOffset.UtcNow));
        _donor = Donor.ForVolunteer(Parish, _volunteer, "Phêrô Minh", DateTimeOffset.UtcNow);
        _donors.Add(_donor);
    }

    private FakeCurrentUser Officer(Guid? id = null) => new(id ?? _officer, GrottoWorksRoles.MaterialOfficer, Parish);

    private RecordDonationReceiptCommandHandler Receive(FakeCurrentUser? user = null)
    {
        var current = user ?? Officer();
        return new(new DonationOfficerScope(new WorkAreaAccess(_scopes, current), _scopes, current), _store, _donors,
            new DonationReceiptPoster(_inventory, new ShortageTracker(new InventoryReader(_inventory, _catalog), _events), _events, current), current);
    }

    private Donation PledgeMaterial(decimal cable = 20, decimal bulb = 10)
    {
        var donation = Donation.PledgeMaterial(_donor.DonorId, _area.SeasonId, _area.WorkAreaId,
            [new PledgedMaterialInput(_cable.MaterialId, cable, null), new PledgedMaterialInput(_bulb.MaterialId, bulb, null)], null, _volunteer, DateTimeOffset.UtcNow);
        _store.Add(donation);
        return donation;
    }

    private Donation PledgeMoney(decimal amount, Guid? workAreaId = null)
    {
        var donation = Donation.PledgeMoney(_donor.DonorId, _area.SeasonId, workAreaId, amount, null, _volunteer, DateTimeOffset.UtcNow);
        _store.Add(donation);
        return donation;
    }

    private static ReceiptLineInput Line(Donation donation, Material material, decimal received, decimal unusable = 0) =>
        new(donation.Items.Single(item => item.MaterialId == material.MaterialId).DonationItemId, received, unusable, null);

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Pledge_delivered_in_two_receipts_moves_to_partially_then_fully_received()
    {
        var pledge = PledgeMaterial();

        // BA 17 #17: 15 received, 3 unusable → 12 posted.
        var first = await Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null, [Line(pledge, _cable, 15, 3)], "Lần 1"));

        first.DonationStatus.Should().Be(DonationStatus.PartiallyReceived);
        first.Items.Single().UsableQuantity.Should().Be(12);
        first.Warnings.Should().BeEmpty();
        _inventory.Ledger.Should().ContainSingle().Which.Quantity.Should().Be(12);
        var cable = pledge.Items.Single(item => item.MaterialId == _cable.MaterialId);
        cable.ReceivedQuantity.Should().Be(15);
        cable.StatusIn(pledge.Status).Should().Be(DonationItemStatus.PartiallyReceived);

        var second = await Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null,
            [Line(pledge, _cable, 5), Line(pledge, _bulb, 10, 1)], null));

        second.DonationStatus.Should().Be(DonationStatus.Received);
        cable.ReceivedQuantity.Should().Be(20);
        cable.UnusableQuantity.Should().Be(3);
        pledge.Receipts.Should().HaveCount(2);
        _inventory.Ledger.Sum(t => t.Quantity).Should().Be(12 + 5 + 9);
        _inventory.Ledger.Select(t => t.ReferenceId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Each_receipt_publishes_contribution_receipt_confirmed_and_the_first_is_flagged()
    {
        var pledge = PledgeMaterial();

        await Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null, [Line(pledge, _cable, 5)], null));
        await Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null, [Line(pledge, _cable, 5)], null));

        var receipts = _events.Events.Where(e => e.EventType == ContributionReceiptConfirmed.EventType)
            .Select(e => (ContributionReceiptConfirmed)e.Payload).ToList();
        receipts.Select(r => r.IsFirstReceipt).Should().Equal(true, false);
        receipts.Should().OnlyContain(r => !r.IsDirect && r.DonorUserId == _volunteer);
        _events.Events.Should().Contain(e => e.EventType == MaterialShortageChanged.EventType);
    }

    [Fact]
    public async Task Receiving_more_than_pledged_is_recorded_with_a_warning()
    {
        var pledge = PledgeMaterial(cable: 10, bulb: 1);

        var result = await Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null,
            [Line(pledge, _cable, 12), Line(pledge, _bulb, 1)], null));

        result.DonationStatus.Should().Be(DonationStatus.Received);
        result.Warnings.Should().ContainSingle();
        pledge.Items.Single(item => item.MaterialId == _cable.MaterialId).ReceivedQuantity.Should().Be(12);
    }

    [Fact]
    public async Task Money_receipts_cumulate_the_amount_received()
    {
        var pledge = PledgeMoney(1_000_000m);

        var first = await Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, 400_000m, [], null));
        first.DonationStatus.Should().Be(DonationStatus.PartiallyReceived);
        first.ReceivedAmount!.Amount.Should().Be(400_000m);

        var second = await Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, 700_000m, [], null));
        second.DonationStatus.Should().Be(DonationStatus.Received);
        second.Warnings.Should().ContainSingle();
        pledge.Amount.Should().Be(1_100_000m);
        pledge.PledgedAmount.Should().Be(1_000_000m);
        _inventory.Ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task Lines_must_belong_to_the_donation_and_respect_type_rules()
    {
        var pledge = PledgeMaterial();
        var money = PledgeMoney(100);

        (await Fails(() => Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null,
            [new ReceiptLineInput(Guid.NewGuid(), 1, 0, null)], null)))).Code.Should().Be("DONATION_ITEM_UNKNOWN");
        (await Fails(() => Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null, [], null))))
            .Code.Should().Be("DONATION_RECEIPT_ITEMS_REQUIRED");
        (await Fails(() => Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null,
            [Line(pledge, _cable, 1), Line(pledge, _cable, 2)], null)))).Code.Should().Be("DONATION_RECEIPT_DUPLICATE_ITEM");
        (await Fails(() => Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null,
            [Line(pledge, _cable, 2, 3)], null)))).Code.Should().Be("DONATION_UNUSABLE_QUANTITY_INVALID");
        (await Fails(() => Receive().HandleAsync(new RecordDonationReceiptCommand(money.DonationId, null, null, [], null))))
            .Code.Should().Be("DONATION_AMOUNT_REQUIRED");
        _inventory.Ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelled_or_fully_received_donations_take_no_more_receipts()
    {
        var pledge = PledgeMaterial(cable: 1, bulb: 1);
        await Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null, [Line(pledge, _cable, 1), Line(pledge, _bulb, 1)], null));
        (await Fails(() => Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null, [Line(pledge, _cable, 1)], null))))
            .Code.Should().Be("DONATION_INVALID_STATE");

        var cancelled = PledgeMoney(100);
        cancelled.CancelPledge("Đổi ý", DateTimeOffset.UtcNow);
        (await Fails(() => Receive().HandleAsync(new RecordDonationReceiptCommand(cancelled.DonationId, null, 50, [], null))))
            .Code.Should().Be("DONATION_INVALID_STATE");
    }

    [Fact]
    public async Task Partially_received_pledge_cancelled_keeps_its_receipts()
    {
        var pledge = PledgeMaterial();
        await Receive().HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null, [Line(pledge, _cable, 5)], null));

        pledge.CancelPledge("Không giao thêm", DateTimeOffset.UtcNow);

        pledge.Status.Should().Be(DonationStatus.Cancelled);
        pledge.Receipts.Should().ContainSingle();
        _inventory.Ledger.Should().ContainSingle().Which.Quantity.Should().Be(5);
        pledge.Items.Single(item => item.MaterialId == _cable.MaterialId).StatusIn(pledge.Status).Should().Be(DonationItemStatus.Cancelled);
    }

    [Fact]
    public async Task Only_the_officer_of_the_receiving_work_area_records()
    {
        var pledge = PledgeMaterial();

        (await Fails(() => Receive(Officer(Guid.NewGuid())).HandleAsync(new RecordDonationReceiptCommand(pledge.DonationId, null, null,
            [Line(pledge, _cable, 1)], null)))).Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        (await Fails(() => Receive().HandleAsync(new RecordDonationReceiptCommand(Guid.NewGuid(), null, null, [], null))))
            .Code.Should().Be("DONATION_NOT_FOUND");
    }
}
