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

/// <summary>F-RES-14: correcting a recorded receipt reconciles stock and tells Reporting.</summary>
public sealed class AdjustDonationTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakeDonorStore _donors = new();
    private readonly FakeDonationStore _store = new();
    private readonly FakeStockLock _lock = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _area;
    private readonly Material _cable;
    private readonly Donor _donor;

    public AdjustDonationTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        _cable = _catalog.SeedMaterial(_catalog.SeedCategory(), "CABLE", "Dây điện", "m");
        _donor = Donor.Create(Parish, null, "Bà Lan", null, null, null, null, _officer, DateTimeOffset.UtcNow);
        _donors.Add(_donor);
    }

    private FakeCurrentUser Officer(Guid? id = null) => new(id ?? _officer, GrottoWorksRoles.MaterialOfficer, Parish);

    private InventoryReader Reader() => new(_inventory, _catalog);

    private AdjustDonationCommandHandler Adjust(FakeCurrentUser? user = null)
    {
        var current = user ?? Officer();
        return new(new MaterialOfficerScope(new WorkAreaAccess(_scopes, current), _scopes, current), _store, _donors, _catalog, Reader(),
            _inventory, _lock, new ShortageTracker(Reader(), _events), new DonationProjector(_donors, _catalog), _events, current);
    }

    /// <summary>Direct MATERIAL donation of 20 m with 2 unusable, already posted (+18).</summary>
    private Donation RecordedCable()
    {
        var donation = Donation.RecordDirect(_donor.DonorId, _area.SeasonId, _area.WorkAreaId, DonationType.Material, null,
            [new ReceivedMaterialInput(_cable.MaterialId, 20, 2, null)], DateTimeOffset.UtcNow, null, _officer, DateTimeOffset.UtcNow);
        _store.Add(donation);
        var line = donation.Receipts.Single().Items.Single();
        _inventory.Append(MaterialTransaction.Post(_area.WorkAreaId, _cable.MaterialId, TransactionType.DonationReceipt, 18, DateTimeOffset.UtcNow,
            _officer, null, TransactionReferenceType.DonationReceiptItem, line.DonationReceiptItemId));
        return donation;
    }

    private static AdjustDonationCommand Correct(Donation donation, decimal received, decimal unusable, string? reason = "Đếm lại") =>
        new(donation.DonationId, null, donation.Receipts.Single().DonationReceiptId, null,
            [new ReceiptLineCorrection(donation.Receipts.Single().Items.Single().DonationReceiptItemId, received, unusable)], reason);

    private decimal Available() => Reader().ReadAsync([_area], _cable.MaterialId).Result.Single().ConfirmedAvailable;

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task More_usable_than_recorded_adds_the_difference_to_stock()
    {
        var donation = RecordedCable();

        var dto = await Adjust().HandleAsync(Correct(donation, 22, 2));

        dto.Items.Single().ReceivedQuantity.Should().Be(22);
        dto.Items.Single().UsableQuantity.Should().Be(20);
        Available().Should().Be(20);
        var correction = _inventory.Ledger.Last();
        correction.Type.Should().Be(TransactionType.DonationReceipt);
        correction.ReferenceType.Should().Be(TransactionReferenceType.InventoryAdjustment);
        correction.Note.Should().Contain("Đếm lại");
        var payload = _events.Events.Single(e => e.EventType == ContributionDonationAdjusted.EventType).Payload.Should().BeOfType<ContributionDonationAdjusted>().Which;
        payload.Items.Single().UsableDelta.Should().Be(2);
        payload.Reason.Should().Be("Đếm lại");
    }

    [Fact]
    public async Task Units_found_unusable_later_leave_stock()
    {
        var donation = RecordedCable();

        await Adjust().HandleAsync(Correct(donation, 20, 5));

        Available().Should().Be(15);
        _inventory.Ledger.Last().Type.Should().Be(TransactionType.AdjustmentDisposed);
        _inventory.Ledger.Last().Quantity.Should().Be(-3);
        _lock.Committed.Should().Be(1);
    }

    [Fact]
    public async Task Stock_already_allocated_cannot_be_corrected_away()
    {
        var donation = RecordedCable();
        _inventory.Allocations.Add(MaterialAllocation.Create(_area.WorkAreaId, null, [new AllocationLineInput(_cable.MaterialId, 17)],
            DateTimeOffset.UtcNow, null, _officer, DateTimeOffset.UtcNow));

        (await Fails(() => Adjust().HandleAsync(Correct(donation, 20, 4)))).Code.Should().Be("MATERIAL_INSUFFICIENT_AVAILABLE");
        Available().Should().Be(18);
    }

    [Fact]
    public async Task Money_correction_changes_the_amount_and_status()
    {
        var pledge = Donation.PledgeMoney(_donor.DonorId, _area.SeasonId, null, 1_000_000m, null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        pledge.RecordReceipt(DateTimeOffset.UtcNow, 1_000_000m, [], null, _officer, DateTimeOffset.UtcNow);
        _store.Add(pledge);

        var dto = await Adjust().HandleAsync(new AdjustDonationCommand(pledge.DonationId, null, pledge.Receipts.Single().DonationReceiptId, 600_000m, [], "Nhầm số"));

        dto.ReceivedAmount!.Amount.Should().Be(600_000m);
        dto.Status.Should().Be(DonationStatus.PartiallyReceived);
        _inventory.Ledger.Should().BeEmpty();
        ((ContributionDonationAdjusted)_events.Events.Single().Payload).AmountDelta.Should().Be(-400_000m);
    }

    [Fact]
    public async Task Reason_receipt_and_values_are_checked()
    {
        var donation = RecordedCable();

        (await Fails(() => Adjust().HandleAsync(Correct(donation, 21, 2, " ")))).Code.Should().Be("DONATION_ADJUSTMENT_REASON_REQUIRED");
        (await Fails(() => Adjust().HandleAsync(Correct(donation, 20, 2)))).Code.Should().Be("DONATION_ADJUSTMENT_EMPTY");
        (await Fails(() => Adjust().HandleAsync(Correct(donation, 3, 4)))).Code.Should().Be("DONATION_UNUSABLE_QUANTITY_INVALID");
        (await Fails(() => Adjust().HandleAsync(Correct(donation, 21, 2) with { ReceiptId = Guid.NewGuid() }))).Code.Should().Be("DONATION_RECEIPT_UNKNOWN");
        (await Fails(() => Adjust(Officer(Guid.NewGuid())).HandleAsync(Correct(donation, 21, 2)))).Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        _inventory.Ledger.Should().ContainSingle();
    }
}
