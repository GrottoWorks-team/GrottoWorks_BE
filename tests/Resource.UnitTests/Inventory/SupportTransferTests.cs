using BuildingBlocks.Security;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Inventory;

/// <summary>F-RES-20: support material transfers — TRANSFER_OUT on approval (BR-63), one-officer receipt (BR-64).</summary>
public sealed class SupportTransferTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _sourceOfficer = Guid.NewGuid();
    private readonly Guid _targetOfficer = Guid.NewGuid();
    private readonly Guid _leader = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakeSupportTransferStore _store = new();
    private readonly FakeStockLock _lock = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _source;
    private readonly WorkAreaScope _target;
    private readonly Material _cable;

    public SupportTransferTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        var season = Guid.NewGuid();
        _source = _scopes.Seed(Parish, _sourceOfficer, season, "Giáo họ A");
        _target = _scopes.Seed(Parish, _targetOfficer, season, "Giáo họ B");
        _cable = _catalog.SeedMaterial(_catalog.SeedCategory(), "CABLE", "Dây điện", "m");
        _inventory.Append(MaterialTransaction.Post(_source.WorkAreaId, _cable.MaterialId, TransactionType.OpeningStock, 40, DateTimeOffset.UtcNow, _sourceOfficer, null));
        _requirements.Add(MaterialRequirement.Create(_target.WorkAreaId, _cable.MaterialId, 30, 0, null, DateTimeOffset.UtcNow));
    }

    private InventoryReader Reader() => new(_inventory, _catalog);

    private StartSupportTransferCommandHandler Start() =>
        new(_scopes, _catalog, Reader(), _inventory, _store, _lock, new ShortageTracker(Reader(), _events));

    private ConfirmTransferReceiptCommandHandler Confirm(Guid? officer = null)
    {
        var user = new FakeCurrentUser(officer ?? _targetOfficer, GrottoWorksRoles.MaterialOfficer, Parish);
        return new(new WorkAreaAccess(_scopes, user), _store, _inventory, new ShortageTracker(Reader(), _events), _events, user);
    }

    private StartSupportTransferCommand Transfer(Guid? itemId = null, decimal quantity = 25) =>
        new(itemId ?? Guid.NewGuid(), _source.WorkAreaId, _target.WorkAreaId, _cable.MaterialId, quantity, _leader);

    private decimal Available(WorkAreaScope area) => Reader().ReadAsync([area], _cable.MaterialId).Result.SingleOrDefault()?.ConfirmedAvailable ?? 0;

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Approval_posts_transfer_out_on_the_source_only()
    {
        var (dto, created) = await Start().HandleAsync(Transfer());

        created.Should().BeTrue();
        dto.Status.Should().Be(SupportTransferStatus.InTransit);
        Available(_source).Should().Be(15);
        Available(_target).Should().Be(0); // in transit: nothing at the target yet
        var posted = _inventory.Ledger.Single(t => t.Type == TransactionType.TransferOut);
        posted.Quantity.Should().Be(-25);
        posted.ReferenceType.Should().Be(TransactionReferenceType.SupportResponseMaterial);
        posted.ReferenceId.Should().Be(dto.SupportResponseMaterialId);
        _lock.Committed.Should().Be(1);
    }

    [Fact]
    public async Task Replays_are_idempotent_and_different_data_conflicts()
    {
        var itemId = Guid.NewGuid();
        await Start().HandleAsync(Transfer(itemId));

        var (_, created) = await Start().HandleAsync(Transfer(itemId));
        created.Should().BeFalse();
        _inventory.Ledger.Count(t => t.Type == TransactionType.TransferOut).Should().Be(1);

        (await Fails(() => Start().HandleAsync(Transfer(itemId, 10)))).Code.Should().Be("SUPPORT_TRANSFER_CONFLICT");
    }

    [Fact]
    public async Task Offer_must_fit_the_unallocated_stock_of_the_source()
    {
        _inventory.Allocations.Add(MaterialAllocation.Create(_source.WorkAreaId, null, [new AllocationLineInput(_cable.MaterialId, 20)],
            DateTimeOffset.UtcNow, null, _sourceOfficer, DateTimeOffset.UtcNow));

        var failure = await Fails(() => Start().HandleAsync(Transfer(quantity: 21)));

        failure.Code.Should().Be("MATERIAL_INSUFFICIENT_AVAILABLE");
        _store.Transfers.Should().BeEmpty();
        Available(_source).Should().Be(40);
    }

    [Fact]
    public async Task Work_areas_must_differ_and_share_the_season()
    {
        var otherSeason = _scopes.Seed(Parish, _targetOfficer);

        (await Fails(() => Start().HandleAsync(Transfer() with { TargetWorkAreaId = otherSeason.WorkAreaId }))).Code.Should().Be("SUPPORT_TRANSFER_OTHER_SEASON");
        (await Fails(() => Start().HandleAsync(Transfer() with { TargetWorkAreaId = _source.WorkAreaId }))).Code.Should().Be("SUPPORT_TRANSFER_SAME_WORK_AREA");
        (await Fails(() => Start().HandleAsync(Transfer() with { TargetWorkAreaId = Guid.NewGuid() }))).Code.Should().Be("WORK_AREA_NOT_PROJECTED");
    }

    [Fact]
    public async Task Receiving_officer_confirms_once_and_only_usable_goes_to_stock()
    {
        var (dto, _) = await Start().HandleAsync(Transfer());
        _events.Events.Clear();

        var confirmed = await Confirm().HandleAsync(new ConfirmTransferReceiptCommand(dto.SupportResponseMaterialId, null, 22, 3, "3 m bị đứt"));

        confirmed.Status.Should().Be(SupportTransferStatus.Received);
        confirmed.ReceivedUsableQuantity.Should().Be(22);
        Available(_target).Should().Be(22);
        var payload = _events.Events.Single(e => e.EventType == SupportTransferConfirmed.EventType).Payload.Should().BeOfType<SupportTransferConfirmed>().Which;
        payload.ReceivedUsableQuantity.Should().Be(22);
        payload.SentQuantity.Should().Be(25);
        _events.Events.Should().Contain(e => e.EventType == MaterialShortageChanged.EventType);

        (await Fails(() => Confirm().HandleAsync(new ConfirmTransferReceiptCommand(dto.SupportResponseMaterialId, null, 1, 0, null))))
            .Code.Should().Be("SUPPORT_TRANSFER_ALREADY_RECEIVED");
    }

    [Fact]
    public async Task Receipt_is_validated_and_only_the_target_officer_confirms()
    {
        var (dto, _) = await Start().HandleAsync(Transfer());

        (await Fails(() => Confirm().HandleAsync(new ConfirmTransferReceiptCommand(dto.SupportResponseMaterialId, null, 20, 6, null))))
            .Code.Should().Be("SUPPORT_TRANSFER_RECEIPT_INVALID");
        (await Fails(() => Confirm(_sourceOfficer).HandleAsync(new ConfirmTransferReceiptCommand(dto.SupportResponseMaterialId, null, 25, 0, null))))
            .Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        (await Fails(() => Confirm().HandleAsync(new ConfirmTransferReceiptCommand(Guid.NewGuid(), null, 1, 0, null))))
            .Code.Should().Be("SUPPORT_TRANSFER_NOT_FOUND");
    }
}
