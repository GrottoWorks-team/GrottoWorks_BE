using BuildingBlocks.Security;
using Resource.Application.Inventory;
using Resource.Application.Purchases;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Purchases;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Purchases;

/// <summary>F-RES-09/10/11: buyer records, officer confirms usable delivery, leader approves cost overrun.</summary>
public sealed class PurchaseTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly Guid _buyer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakePurchaseRequestStore _requests = new();
    private readonly FakePurchaseStore _purchases = new();
    private readonly FakeTaskProjectionStore _tasks = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _area;
    private readonly Guid _leader;
    private readonly Material _cable;
    private readonly PurchaseRequest _request;
    private readonly TaskProjection _task;

    public PurchaseTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        _leader = _area.LeaderUserId!.Value;
        _cable = _catalog.SeedMaterial(_catalog.SeedCategory(), "CABLE", "Dây điện", "m");
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 50, 0, null, DateTimeOffset.UtcNow));

        // Approved request: 50 m estimated at 500,000 VND.
        _request = PurchaseRequest.Create(_area.WorkAreaId, _officer, "Thiếu dây",
            [new PurchaseRequestItemInput(_cable.MaterialId, 50, 500_000m, null, null)], DateTimeOffset.UtcNow);
        _request.Submit(_officer, DateTimeOffset.UtcNow);
        _request.Approve(_leader, null, DateTimeOffset.UtcNow);
        _requests.Add(_request);

        var sync = new PurchaseBuyerSync(_requests);
        _task = TaskProjection.FromSnapshot(new TaskSnapshot(Guid.NewGuid(), _area.WorkAreaId, "Mua dây", ProjectedTaskType.Purchase,
            ProjectedTaskStatus.InProgress, _request.PurchaseRequestId, [_buyer], 1), DateTimeOffset.UtcNow);
        _tasks.Add(_task);
        sync.ApplyAsync(_task, CancellationToken.None).GetAwaiter().GetResult();
    }

    private FakeCurrentUser User(Guid id, string role) => new(id, role, Parish);

    private PurchaseRequestLoader Loader(FakeCurrentUser user) => new(_requests, new WorkAreaAccess(_scopes, user), _catalog);

    private PurchaseContext Context(FakeCurrentUser user) => new(_purchases, Loader(user));

    private RecordPurchaseCommandHandler Record(Guid? buyer = null)
    {
        var user = User(buyer ?? _buyer, GrottoWorksRoles.Volunteer);
        return new(Loader(user), _tasks, _purchases, Context(user), user);
    }

    private ConfirmPurchaseDeliveryCommandHandler Confirm(Guid? officer = null)
    {
        var user = User(officer ?? _officer, GrottoWorksRoles.MaterialOfficer);
        var reader = new InventoryReader(_inventory, _catalog);
        return new(Context(user), _purchases, new WorkAreaAccess(_scopes, user), _inventory, new ShortageTracker(reader, _events), _events, user);
    }

    private PurchaseReviewCommandHandler Review(Guid userId, string role)
    {
        var user = User(userId, role);
        return new(Context(user), _purchases, new WorkAreaAccess(_scopes, user), user);
    }

    private RecordPurchaseCommand Bought(decimal quantity, decimal unitPrice = 10_000m) =>
        new(_request.PurchaseRequestId, "Điện Quang", null,
            [new PurchaseLineInput(_request.Items.Single().PurchaseRequestItemId, quantity, unitPrice, null)], []);

    private static ConfirmDeliveryCommand Delivered(PurchaseDto purchase, decimal usable, decimal unusable = 0) =>
        new(purchase.Id, null, [new DeliveryLineInput(purchase.Items.Single().Id, usable, unusable)]);

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public void Accepted_purchase_task_marks_the_request_buyer_assigned()
    {
        _request.Status.Should().Be(PurchaseRequestStatus.BuyerAssigned);
    }

    [Fact]
    public async Task Buyer_records_the_purchase_without_touching_stock()
    {
        var dto = await Record().HandleAsync(Bought(50));

        dto.Status.Should().Be(PurchaseStatus.Submitted);
        dto.TotalCost.Amount.Should().Be(500_000m);
        dto.TaskId.Should().Be(_task.TaskId);
        dto.CostOverrun.Should().BeFalse();
        _request.Status.Should().Be(PurchaseRequestStatus.Purchased);
        _inventory.Ledger.Should().BeEmpty(); // BR-26
    }

    [Fact]
    public async Task Only_the_accepted_buyer_records()
    {
        (await Fails(() => Record(Guid.NewGuid()).HandleAsync(Bought(1)))).Code.Should().Be("PURCHASE_NOT_BUYER");
        (await Fails(() => Record().HandleAsync(Bought(1) with { Items = [new PurchaseLineInput(Guid.NewGuid(), 1, 1, null)] })))
            .Code.Should().Be("PURCHASE_REQUEST_ITEM_UNKNOWN");
        (await Fails(() => Record().HandleAsync(Bought(1) with { PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)) })))
            .Code.Should().Be("PURCHASE_DATE_IN_FUTURE");
    }

    [Fact]
    public async Task Short_delivery_posts_usable_and_keeps_the_request_open_then_completes()
    {
        var first = await Record().HandleAsync(Bought(30));
        var confirmed = await Confirm().HandleAsync(Delivered(first, 28, 2));

        confirmed.Status.Should().Be(PurchaseStatus.Confirmed);
        _request.Status.Should().Be(PurchaseRequestStatus.PartiallyReceived);
        var posted = _inventory.Ledger.Single();
        posted.Type.Should().Be(TransactionType.PurchaseDelivery);
        posted.Quantity.Should().Be(28);
        posted.ReferenceType.Should().Be(TransactionReferenceType.PurchaseItem);
        var payload = _events.Events.Single(e => e.EventType == PurchaseDeliveryConfirmed.EventType).Payload.Should().BeOfType<PurchaseDeliveryConfirmed>().Which;
        payload.TaskId.Should().Be(_task.TaskId);
        payload.RequestStatus.Should().Be("PARTIALLY_RECEIVED");

        var second = await Record().HandleAsync(Bought(22, 9_000m)); // 300,000 + 198,000 stays within the 500,000 estimate
        await Confirm().HandleAsync(Delivered(second, 22));
        _request.Status.Should().Be(PurchaseRequestStatus.Completed);
        _inventory.Ledger.Sum(t => t.Quantity).Should().Be(50);
    }

    [Fact]
    public async Task Delivery_is_validated_and_confirmed_once_by_the_officer_of_the_area()
    {
        var purchase = await Record().HandleAsync(Bought(10));

        (await Fails(() => Confirm().HandleAsync(Delivered(purchase, 9, 2)))).Code.Should().Be("PURCHASE_DELIVERY_QUANTITY_INVALID");
        (await Fails(() => Confirm(Guid.NewGuid()).HandleAsync(Delivered(purchase, 10)))).Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        await Confirm().HandleAsync(Delivered(purchase, 10));
        (await Fails(() => Confirm().HandleAsync(Delivered(purchase, 10)))).Code.Should().Be("PURCHASE_INVALID_STATE");
    }

    [Fact]
    public async Task Cost_above_the_estimate_needs_the_leader_before_confirmation()
    {
        var purchase = await Record().HandleAsync(Bought(50, 11_000m)); // 550,000 > 500,000

        purchase.CostOverrun.Should().BeTrue();
        (await Fails(() => Confirm().HandleAsync(Delivered(purchase, 50)))).Code.Should().Be("PURCHASE_COST_OVERRUN_NOT_APPROVED");
        (await Fails(() => Review(_officer, GrottoWorksRoles.MaterialOfficer).ApproveCostOverrunAsync(new PurchaseDecisionCommand(purchase.Id, null, null))))
            .Code.Should().Be("WORK_AREA_SCOPE_DENIED");

        var approved = await Review(_leader, GrottoWorksRoles.Leader).ApproveCostOverrunAsync(new PurchaseDecisionCommand(purchase.Id, null, "Giá tăng"));
        approved.CostOverrunApprovedBy.Should().Be(_leader);
        (await Confirm().HandleAsync(Delivered(purchase, 50))).Status.Should().Be(PurchaseStatus.Confirmed);
    }

    [Fact]
    public async Task Overrun_approval_only_when_the_cost_is_above_the_estimate()
    {
        var purchase = await Record().HandleAsync(Bought(10));

        (await Fails(() => Review(_leader, GrottoWorksRoles.Leader).ApproveCostOverrunAsync(new PurchaseDecisionCommand(purchase.Id, null, null))))
            .Code.Should().Be("PURCHASE_NO_COST_OVERRUN");
    }

    [Fact]
    public async Task Invalid_receipt_goes_to_needs_info_and_back_with_new_files()
    {
        var purchase = await Record().HandleAsync(Bought(10));

        (await Fails(() => Review(_officer, GrottoWorksRoles.MaterialOfficer).RequestInfoAsync(new PurchaseDecisionCommand(purchase.Id, null, ""))))
            .Code.Should().Be("PURCHASE_INFO_REASON_REQUIRED");
        var needsInfo = await Review(_officer, GrottoWorksRoles.MaterialOfficer).RequestInfoAsync(new PurchaseDecisionCommand(purchase.Id, null, "Hóa đơn mờ"));
        needsInfo.Status.Should().Be(PurchaseStatus.NeedsInfo);
        (await Fails(() => Confirm().HandleAsync(Delivered(purchase, 10)))).Code.Should().Be("PURCHASE_INVALID_STATE");

        var buyer = User(_buyer, GrottoWorksRoles.Volunteer);
        var resubmitted = await new AttachPurchaseReceiptsCommandHandler(Context(buyer), _purchases, buyer)
            .HandleAsync(new AttachPurchaseReceiptsCommand(purchase.Id, [Guid.NewGuid()]));
        resubmitted.Status.Should().Be(PurchaseStatus.Submitted);
        resubmitted.ReceiptFileIds.Should().ContainSingle();
        resubmitted.InfoRequestReason.Should().BeNull();

        var stranger = User(Guid.NewGuid(), GrottoWorksRoles.Volunteer);
        (await Fails(() => new AttachPurchaseReceiptsCommandHandler(Context(stranger), _purchases, stranger)
            .HandleAsync(new AttachPurchaseReceiptsCommand(purchase.Id, [Guid.NewGuid()])))).Code.Should().Be("PURCHASE_NOT_FOUND");
    }

    [Fact]
    public async Task Buyer_sees_the_request_with_its_purchases()
    {
        await Record().HandleAsync(Bought(10));
        var buyer = User(_buyer, GrottoWorksRoles.Volunteer);

        var dto = await new GetPurchaseRequestQueryHandler(Loader(buyer), _tasks, _purchases, buyer).HandleAsync(_request.PurchaseRequestId);

        dto.Purchases.Should().ContainSingle().Which.BuyerUserId.Should().Be(_buyer);
    }
}
