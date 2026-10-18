using BuildingBlocks.Security;
using Resource.Application.Inventory;
using Resource.Application.Purchases;
using Resource.Application.Scope;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Purchases;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Purchases;

/// <summary>F-RES-06/07: purchase requests — draft, shortage guard, submit, leader approve/reject, history.</summary>
public sealed class PurchaseRequestTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakePurchaseRequestStore _store = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _area;
    private readonly Guid _leader;
    private readonly Material _cable;
    private readonly Material _paint;

    public PurchaseRequestTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        _leader = _area.LeaderUserId!.Value;
        var category = _catalog.SeedCategory();
        _cable = _catalog.SeedMaterial(category, "CABLE", "Dây điện", "m");
        _paint = _catalog.SeedMaterial(category, "PAINT", "Sơn", "lít");

        // Cable: required 50, 20 in stock → shortage 30. Paint: no requirement → shortage 0.
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 50, 0, null, DateTimeOffset.UtcNow));
        _inventory.Append(MaterialTransaction.Post(
            _area.WorkAreaId, _cable.MaterialId, TransactionType.OpeningStock, 20, DateTimeOffset.UtcNow, _officer, null));
    }

    private FakeCurrentUser User(Guid? id = null, string role = GrottoWorksRoles.MaterialOfficer, Guid? parish = null) =>
        new(id ?? _officer, role, parish ?? Parish);

    private FakeCurrentUser Leader(Guid? id = null) => User(id ?? _leader, GrottoWorksRoles.Leader);

    private WorkAreaAccess Access(ICurrentUser user) => new(_scopes, user);

    private PurchaseRequestLoader Loader(ICurrentUser user) => new(_store, Access(user), _catalog);

    private PurchaseShortageGuard Guard() => new(new InventoryReader(_inventory, _catalog), _store);

    private CreatePurchaseRequestCommandHandler Create(FakeCurrentUser? user = null)
    {
        var current = user ?? User();
        return new(Access(current), _store, _catalog, Guard(), Loader(current), current);
    }

    private UpdatePurchaseRequestCommandHandler Update(FakeCurrentUser? user = null)
    {
        var current = user ?? User();
        return new(Access(current), _store, _catalog, Guard(), Loader(current));
    }

    private SubmitPurchaseRequestCommandHandler Submit(FakeCurrentUser? user = null)
    {
        var current = user ?? User();
        return new(Access(current), _store, Guard(), Loader(current), _events, current);
    }

    private DecidePurchaseRequestCommandHandler Decide(FakeCurrentUser? user = null)
    {
        var current = user ?? Leader();
        return new(Access(current), _store, Loader(current), _events, current);
    }

    private static PurchaseRequestItemInput Line(Material material, decimal quantity, decimal? cost = null, string? overrideReason = null) =>
        new(material.MaterialId, quantity, cost, null, overrideReason);

    private Task<PurchaseRequestDto> Draft(params PurchaseRequestItemInput[] items) =>
        Create().HandleAsync(new CreatePurchaseRequestCommand(_area.WorkAreaId, "Thiếu dây cho hang đá", items));

    private async Task<PurchaseRequestDto> Pending(params PurchaseRequestItemInput[] items)
    {
        var draft = await Draft(items);
        return await Submit().HandleAsync(new PurchaseRequestCommand(draft.Id, draft.Version));
    }

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Officer_creates_a_draft_with_estimated_total()
    {
        var dto = await Draft(Line(_cable, 30, 450_000m), Line(_paint, 2, 120_000m, "Sơn lại tượng"));

        dto.Status.Should().Be(PurchaseRequestStatus.Draft);
        dto.RequestedBy.Should().Be(_officer);
        dto.SeasonId.Should().Be(_area.SeasonId);
        dto.EstimatedTotal.Should().Be(new MoneyDto(570_000m, "VND"));
        dto.Items.Select(item => item.MaterialCode).Should().Equal("CABLE", "PAINT");
        dto.Items[0].Unit.Should().Be("m");
        _store.History.Should().ContainSingle(change => change.FromStatus == null && change.ToStatus == PurchaseRequestStatus.Draft);
        _events.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task A_draft_may_start_without_lines_or_reason()
    {
        var dto = await Create().HandleAsync(new CreatePurchaseRequestCommand(_area.WorkAreaId, null, []));

        dto.Items.Should().BeEmpty();
        dto.EstimatedTotal.Amount.Should().Be(0);
    }

    [Theory]
    [InlineData(0, null, "PURCHASE_ITEM_QUANTITY_INVALID")]
    [InlineData(1.001, null, "PURCHASE_ITEM_QUANTITY_INVALID")]
    [InlineData(1, -1.0, "PURCHASE_ITEM_COST_INVALID")]
    [InlineData(1, 0.001, "PURCHASE_ITEM_COST_INVALID")]
    public async Task Lines_are_validated(decimal quantity, double? cost, string code)
    {
        (await Fails(() => Draft(Line(_cable, quantity, (decimal?)cost)))).Code.Should().Be(code);
    }

    [Fact]
    public async Task A_material_appears_once_and_must_be_active()
    {
        (await Fails(() => Draft(Line(_cable, 1), Line(_cable, 2)))).Code.Should().Be("PURCHASE_REQUEST_DUPLICATE_MATERIAL");

        (await Fails(() => Draft(new PurchaseRequestItemInput(Guid.NewGuid(), 1, null, null, "x")))).Code.Should().Be("MATERIAL_UNKNOWN");

        _cable.Update(null, null, null, CatalogStatus.Inactive, isReferenced: true);
        (await Fails(() => Draft(Line(_cable, 1)))).Code.Should().Be("MATERIAL_INACTIVE");
    }

    [Fact]
    public async Task Only_the_assigned_officer_of_a_writable_season()
    {
        (await Fails(() => Create(User(Guid.NewGuid())).HandleAsync(
            new CreatePurchaseRequestCommand(_area.WorkAreaId, null, [])))).Code.Should().Be("WORK_AREA_SCOPE_DENIED");

        (await Fails(() => Create(User(parish: Guid.NewGuid())).HandleAsync(
            new CreatePurchaseRequestCommand(_area.WorkAreaId, null, [])))).Kind.Should().Be(DomainErrorKind.NotFound);

        var archived = _scopes.Seed(Parish, _officer, seasonStatus: SeasonStatus.Archived);
        (await Fails(() => Create().HandleAsync(new CreatePurchaseRequestCommand(archived.WorkAreaId, null, []))))
            .Code.Should().Be("SEASON_READ_ONLY");
    }

    [Fact]
    public async Task Lines_cannot_exceed_the_shortage_without_an_override_reason()
    {
        (await Draft(Line(_cable, 30))).Items.Should().ContainSingle();

        (await Fails(() => Draft(Line(_cable, 30.5m)))).Code.Should().Be("PURCHASE_EXCEEDS_SHORTAGE");

        // Paint has no shortage at all (BA 8.6: shortage = 0 → override + reason).
        (await Fails(() => Draft(Line(_paint, 1)))).Code.Should().Be("PURCHASE_EXCEEDS_SHORTAGE");
        (await Draft(Line(_paint, 1, overrideReason: "Dự phòng hư hỏng")))
            .Items.Single().ShortageOverrideReason.Should().Be("Dự phòng hư hỏng");
    }

    [Fact]
    public async Task Open_requests_already_cover_part_of_the_shortage()
    {
        await Pending(Line(_cable, 20));

        // 30 short, 20 already asked for and waiting for approval → only 10 left.
        (await Fails(() => Draft(Line(_cable, 15)))).Code.Should().Be("PURCHASE_EXCEEDS_SHORTAGE");
        (await Draft(Line(_cable, 10))).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Drafts_and_rejected_requests_do_not_reserve_the_shortage()
    {
        await Draft(Line(_cable, 30));
        var rejected = await Pending(Line(_cable, 30));
        await Decide().RejectAsync(new PurchaseRequestCommand(rejected.Id, rejected.Version, "Đã có người tặng"));

        (await Draft(Line(_cable, 30))).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Submit_needs_lines_and_a_reason_then_notifies_the_leader()
    {
        var empty = await Create().HandleAsync(new CreatePurchaseRequestCommand(_area.WorkAreaId, "Lý do", []));
        (await Fails(() => Submit().HandleAsync(new PurchaseRequestCommand(empty.Id, null)))).Code.Should().Be("PURCHASE_REQUEST_ITEMS_REQUIRED");

        var noReason = await Create().HandleAsync(new CreatePurchaseRequestCommand(_area.WorkAreaId, "  ", [Line(_cable, 5)]));
        (await Fails(() => Submit().HandleAsync(new PurchaseRequestCommand(noReason.Id, null)))).Code.Should().Be("PURCHASE_REQUEST_REASON_REQUIRED");

        var submitted = await Pending(Line(_cable, 5, 75_000m));

        submitted.Status.Should().Be(PurchaseRequestStatus.PendingApproval);
        var published = _events.Events.Should().ContainSingle().Which;
        published.EventType.Should().Be("purchase.request.submitted");
        published.AggregateId.Should().Be(submitted.Id);
        var payload = published.Payload.Should().BeOfType<PurchaseRequestSubmitted>().Which;
        payload.LeaderUserId.Should().Be(_leader);
        payload.ParishId.Should().Be(Parish);
        payload.EstimatedTotal.Should().Be(75_000m);
        payload.Items.Should().ContainSingle(line => line.MaterialId == _cable.MaterialId && line.RequestedQuantity == 5);
    }

    [Fact]
    public async Task Submit_rechecks_the_shortage_against_current_stock()
    {
        var draft = await Draft(Line(_cable, 30));
        _inventory.Append(MaterialTransaction.Post(
            _area.WorkAreaId, _cable.MaterialId, TransactionType.DonationReceipt, 10, DateTimeOffset.UtcNow, _officer, null,
            TransactionReferenceType.DonationReceiptItem, Guid.NewGuid()));

        (await Fails(() => Submit().HandleAsync(new PurchaseRequestCommand(draft.Id, null)))).Code.Should().Be("PURCHASE_EXCEEDS_SHORTAGE");
    }

    [Fact]
    public async Task Only_drafts_are_edited_or_submitted()
    {
        var pending = await Pending(Line(_cable, 5));

        (await Fails(() => Submit().HandleAsync(new PurchaseRequestCommand(pending.Id, null)))).Code.Should().Be("PURCHASE_REQUEST_INVALID_STATE");
        (await Fails(() => Update().HandleAsync(new UpdatePurchaseRequestCommand(pending.Id, null, "Khác", null))))
            .Code.Should().Be("PURCHASE_REQUEST_INVALID_STATE");
    }

    [Fact]
    public async Task Update_replaces_lines_and_clears_an_empty_reason()
    {
        var draft = await Draft(Line(_cable, 5));

        var updated = await Update().HandleAsync(
            new UpdatePurchaseRequestCommand(draft.Id, draft.Version, "", [Line(_cable, 8, 100m), Line(_paint, 1, 50m, "Dự phòng")]));

        updated.Reason.Should().BeNull();
        updated.Items.Should().HaveCount(2);
        updated.Items.Single(item => item.MaterialId == _cable.MaterialId).RequestedQuantity.Should().Be(8);
        updated.EstimatedTotal.Amount.Should().Be(150m);
    }

    [Fact]
    public async Task Stale_version_is_rejected()
    {
        var draft = await Draft(Line(_cable, 5));

        var failure = await Fails(() => Update().HandleAsync(new UpdatePurchaseRequestCommand(draft.Id, draft.Version + 1, "x", null)));

        failure.Code.Should().Be("PURCHASE_REQUEST_VERSION_MISMATCH");
        failure.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Leader_of_the_work_area_approves_and_Task_is_told()
    {
        var pending = await Pending(Line(_cable, 5));
        _events.Events.Clear();

        var approved = await Decide().ApproveAsync(new PurchaseRequestCommand(pending.Id, pending.Version, " Đồng ý "));

        approved.Status.Should().Be(PurchaseRequestStatus.Approved);
        approved.ApprovedBy.Should().Be(_leader);
        approved.ApprovedAt.Should().NotBeNull();
        approved.DecisionNote.Should().Be("Đồng ý");
        var payload = _events.Events.Should().ContainSingle(e => e.EventType == "purchase.request.decided")
            .Which.Payload.Should().BeOfType<PurchaseRequestDecided>().Which;
        payload.Decision.Should().Be("APPROVED");
        payload.DecidedByUserId.Should().Be(_leader);
        payload.WorkAreaId.Should().Be(_area.WorkAreaId);
    }

    [Fact]
    public async Task Another_leader_cannot_decide_and_a_draft_cannot_be_approved()
    {
        var pending = await Pending(Line(_cable, 5));
        (await Fails(() => Decide(Leader(Guid.NewGuid())).ApproveAsync(new PurchaseRequestCommand(pending.Id, null))))
            .Code.Should().Be("WORK_AREA_SCOPE_DENIED");

        var draft = await Draft(Line(_cable, 5));
        (await Fails(() => Decide().ApproveAsync(new PurchaseRequestCommand(draft.Id, null)))).Code.Should().Be("PURCHASE_REQUEST_INVALID_STATE");

        await Decide().ApproveAsync(new PurchaseRequestCommand(pending.Id, null));
        (await Fails(() => Decide().RejectAsync(new PurchaseRequestCommand(pending.Id, null, "Muộn"))))
            .Code.Should().Be("PURCHASE_REQUEST_INVALID_STATE");
    }

    [Fact]
    public async Task Rejection_needs_a_reason()
    {
        var pending = await Pending(Line(_cable, 5));

        (await Fails(() => Decide().RejectAsync(new PurchaseRequestCommand(pending.Id, null, "   "))))
            .Code.Should().Be("PURCHASE_REQUEST_REJECT_REASON_REQUIRED");

        var rejected = await Decide().RejectAsync(new PurchaseRequestCommand(pending.Id, null, "Vượt ngân sách"));

        rejected.Status.Should().Be(PurchaseRequestStatus.Rejected);
        rejected.ApprovedBy.Should().Be(_leader);
        rejected.RejectedAt.Should().NotBeNull();
        rejected.DecisionNote.Should().Be("Vượt ngân sách");
        _events.Events.Last().Payload.Should().BeOfType<PurchaseRequestDecided>().Which.Decision.Should().Be("REJECTED");
    }

    [Fact]
    public async Task History_lists_every_transition_in_order()
    {
        var pending = await Pending(Line(_cable, 5));
        await Decide().ApproveAsync(new PurchaseRequestCommand(pending.Id, null, "OK"));

        var history = await new GetPurchaseRequestHistoryQueryHandler(Loader(Leader()), _store).HandleAsync(pending.Id);

        history.Select(change => change.ToStatus).Should().Equal(
            PurchaseRequestStatus.Draft, PurchaseRequestStatus.PendingApproval, PurchaseRequestStatus.Approved);
        history[0].FromStatus.Should().BeNull();
        history[1].ChangedBy.Should().Be(_officer);
        history[2].ChangedBy.Should().Be(_leader);
        history[2].Note.Should().Be("OK");
    }

    [Fact]
    public async Task Reads_are_limited_to_the_parish_and_hidden_from_volunteers_for_now()
    {
        var draft = await Draft(Line(_cable, 5));

        var parishUser = User(Guid.NewGuid(), GrottoWorksRoles.Parish);
        (await new GetPurchaseRequestQueryHandler(Loader(parishUser), parishUser).HandleAsync(draft.Id)).Id.Should().Be(draft.Id);

        var outsider = User(Guid.NewGuid(), GrottoWorksRoles.Parish, Guid.NewGuid());
        (await Fails(() => new GetPurchaseRequestQueryHandler(Loader(outsider), outsider).HandleAsync(draft.Id)))
            .Code.Should().Be("PURCHASE_REQUEST_NOT_FOUND");

        var volunteer = User(Guid.NewGuid(), GrottoWorksRoles.Volunteer);
        (await Fails(() => new GetPurchaseRequestQueryHandler(Loader(volunteer), volunteer).HandleAsync(draft.Id)))
            .Code.Should().Be("PURCHASE_REQUEST_NOT_FOUND");
    }

    [Fact]
    public async Task List_filters_by_status_within_the_parish()
    {
        await Draft(Line(_cable, 5));
        var pending = await Pending(Line(_cable, 5));
        var otherParish = _scopes.Seed(Guid.NewGuid(), _officer);
        _store.Add(PurchaseRequest.Create(otherParish.WorkAreaId, _officer, "x", [], DateTimeOffset.UtcNow));

        var leader = Leader();
        var handler = new ListPurchaseRequestsQueryHandler(Access(leader), _scopes, _store, Loader(leader));

        var all = await handler.HandleAsync(new PurchaseRequestFilter(1, 20, null, null, null, true));
        all.TotalItems.Should().Be(2);

        var waiting = await handler.HandleAsync(new PurchaseRequestFilter(1, 20, null, _area.WorkAreaId, PurchaseRequestStatus.PendingApproval, true));
        waiting.Items.Should().ContainSingle().Which.Id.Should().Be(pending.Id);
    }
}
