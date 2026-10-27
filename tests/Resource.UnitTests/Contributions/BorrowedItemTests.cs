using BuildingBlocks.Security;
using Resource.Application.Contributions;
using Resource.Application.Scope;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Contributions;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Contributions;

/// <summary>F-RES-18: loan offers and borrowed items — BR-41..43, D-05, reminders.</summary>
public sealed class BorrowedItemTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly Guid _volunteer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeTaskProjectionStore _tasks = new();
    private readonly FakeBorrowedItemStore _store = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _area;
    private readonly DateOnly _today = ParishClock.Today(DateTimeOffset.UtcNow);

    public BorrowedItemTests() => _area = _scopes.Seed(Parish, _officer);

    private FakeCurrentUser Volunteer() => new(_volunteer, GrottoWorksRoles.Volunteer, Parish);

    private FakeCurrentUser Officer(Guid? id = null) => new(id ?? _officer, GrottoWorksRoles.MaterialOfficer, Parish);

    private BorrowedItemAccess Access(FakeCurrentUser user) =>
        new(_store, new MaterialOfficerScope(new WorkAreaAccess(_scopes, user), _scopes, user), new WorkAreaAccess(_scopes, user), _scopes);

    private OfferItemForLoanCommandHandler Offer(FakeCurrentUser? user = null)
    {
        var current = user ?? Volunteer();
        return new(new WorkAreaAccess(_scopes, current), _scopes, _store, _events, current);
    }

    private BorrowedItemCommandHandler Commands(FakeCurrentUser? user = null)
    {
        var current = user ?? Officer();
        return new(Access(current), _store, _tasks, current);
    }

    private LoanOfferInput Input(string? owner = "Ông Giuse Tâm", int daysUntilReturn = 30, Guid? workAreaId = null) =>
        new(_area.SeasonId, workAreaId, owner, "0909 111 222", "5 Hai Bà Trưng", null, "Máy phát điện 5kVA", 1, null, "Tốt",
            _today.AddDays(daysUntilReturn));

    private async Task<BorrowedItemDto> IssuedItem(int daysUntilReturn = 30)
    {
        var offer = await Offer().HandleAsync(new OfferItemForLoanCommand(Input(daysUntilReturn: daysUntilReturn)));
        await Commands().AcceptAsync(new AcceptLoanOfferCommand(offer.Id, null, _area.WorkAreaId, null));
        return await Commands().IssueAsync(new IssueBorrowedItemCommand(offer.Id, null, null, null));
    }

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Volunteer_offers_on_behalf_of_the_lender_and_officers_are_told()
    {
        var dto = await Offer().HandleAsync(new OfferItemForLoanCommand(Input()));

        dto.Status.Should().Be(BorrowedItemStatus.Offered);
        dto.OfferedBy.Should().Be(_volunteer);
        dto.OwnerName.Should().Be("Ông Giuse Tâm");
        var payload = _events.Events.Single().Payload.Should().BeOfType<LoanOfferSubmitted>().Which;
        payload.MaterialOfficerUserIds.Should().Equal(_officer);
        payload.ParishId.Should().Be(Parish);
    }

    [Fact]
    public async Task Lender_name_and_a_future_return_date_are_required()
    {
        (await Fails(() => Offer().HandleAsync(new OfferItemForLoanCommand(Input(owner: " "))))).Code.Should().Be("LOAN_OWNER_NAME_REQUIRED");
        (await Fails(() => Offer().HandleAsync(new OfferItemForLoanCommand(Input(daysUntilReturn: -1))))).Code.Should().Be("LOAN_RETURN_DATE_IN_PAST");
        (await Fails(() => Offer().HandleAsync(new OfferItemForLoanCommand(Input() with { SeasonId = Guid.NewGuid() })))).Code.Should().Be("SEASON_NOT_FOUND");
        (await Fails(() => Offer().HandleAsync(new OfferItemForLoanCommand(Input(workAreaId: Guid.NewGuid()))))).Code.Should().Be("LOAN_SEASON_MISMATCH");
    }

    [Fact]
    public async Task Full_lifecycle_offer_accept_issue_assign_return()
    {
        var task = _tasks.Seed(_area.WorkAreaId);
        var issued = await IssuedItem();
        issued.Status.Should().Be(BorrowedItemStatus.Issued);
        issued.Transactions.Should().ContainSingle().Which.Kind.Should().Be("ISSUE");

        var inUse = await Commands().AssignAsync(new AssignBorrowedItemCommand(issued.Id, null, _area.WorkAreaId, task.TaskId));
        inUse.Status.Should().Be(BorrowedItemStatus.InUse);
        inUse.TaskId.Should().Be(task.TaskId);

        var returned = await Commands().ReturnAsync(new ReturnBorrowedItemCommand(issued.Id, null, null, ReturnCondition.Good, null, "Đủ"));
        returned.Status.Should().Be(BorrowedItemStatus.Returned);
        var row = returned.Transactions.Single();
        row.Kind.Should().Be("RETURN");
        row.Condition.Should().Be(ReturnCondition.Good);
        row.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task Rejection_needs_a_reason_and_only_offers_are_decided()
    {
        var offer = await Offer().HandleAsync(new OfferItemForLoanCommand(Input()));
        (await Fails(() => Commands().RejectAsync(new RejectLoanOfferCommand(offer.Id, null, "")))).Code.Should().Be("LOAN_REJECT_REASON_REQUIRED");

        var rejected = await Commands().RejectAsync(new RejectLoanOfferCommand(offer.Id, null, "Không cần máy phát"));
        rejected.Status.Should().Be(BorrowedItemStatus.Rejected);
        rejected.RejectionReason.Should().Be("Không cần máy phát");
        (await Fails(() => Commands().AcceptAsync(new AcceptLoanOfferCommand(offer.Id, null, null, null)))).Code.Should().Be("BORROWED_ITEM_INVALID_STATE");
        (await Fails(() => Commands().IssueAsync(new IssueBorrowedItemCommand(offer.Id, null, null, null)))).Code.Should().Be("BORROWED_ITEM_INVALID_STATE");
    }

    [Fact]
    public async Task Incidents_are_logged_while_out_and_lost_returns_record_zero_back()
    {
        var issued = await IssuedItem();

        (await Fails(() => Commands().RecordIncidentAsync(new BorrowedItemIncidentCommand(issued.Id, ReturnCondition.Good, null, null, "x"))))
            .Code.Should().Be("LOAN_INCIDENT_CONDITION_INVALID");
        var incident = await Commands().RecordIncidentAsync(new BorrowedItemIncidentCommand(issued.Id, ReturnCondition.Damaged, null, null, "Cháy cầu chì"));
        incident.Kind.Should().Be("INCIDENT");
        incident.Condition.Should().Be(ReturnCondition.Damaged);

        var returned = await Commands().ReturnAsync(new ReturnBorrowedItemCommand(issued.Id, null, null, ReturnCondition.Lost, null, null));
        returned.Transactions.Should().HaveCount(2);
        returned.Transactions.Single(t => t.Kind == "RETURN").Quantity.Should().Be(0);
        (await Fails(() => Commands().RecordIncidentAsync(new BorrowedItemIncidentCommand(issued.Id, ReturnCondition.Lost, null, null, "x"))))
            .Code.Should().Be("BORROWED_ITEM_INVALID_STATE");
    }

    [Fact]
    public async Task Dates_and_scope_are_checked()
    {
        var issued = await IssuedItem();

        (await Fails(() => Commands().ReturnAsync(new ReturnBorrowedItemCommand(issued.Id, null, _today.AddDays(1), ReturnCondition.Good, null, null))))
            .Code.Should().Be("LOAN_RETURN_DATE_INVALID");
        (await Fails(() => Commands(Officer(Guid.NewGuid())).ReturnAsync(new ReturnBorrowedItemCommand(issued.Id, null, null, ReturnCondition.Good, null, null))))
            .Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        var otherSeason = _scopes.Seed(Parish, _officer);
        (await Fails(() => Commands().AssignAsync(new AssignBorrowedItemCommand(issued.Id, null, otherSeason.WorkAreaId, null))))
            .Code.Should().Be("LOAN_SEASON_MISMATCH");
        (await Fails(() => Commands().AssignAsync(new AssignBorrowedItemCommand(issued.Id, null, _area.WorkAreaId, Guid.NewGuid()))))
            .Code.Should().Be("TASK_UNKNOWN");
    }

    [Fact]
    public async Task Reminder_job_sends_one_reminder_per_item_per_day()
    {
        var dueTomorrow = await IssuedItem(daysUntilReturn: 1);
        var overdue = await IssuedItem(daysUntilReturn: 0);
        await IssuedItem(daysUntilReturn: 10);
        _events.Events.Clear();

        var job = new BorrowedItemReminderJob(_store, _scopes, _events);
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1);
        (await job.RunAsync(tomorrow)).Should().Be(2);
        (await job.RunAsync(tomorrow)).Should().Be(0);

        _events.Events.Should().Contain(e => e.EventType == BorrowedItemReminder.ReturnDueEventType && e.AggregateId == dueTomorrow.Id);
        var late = _events.Events.Single(e => e.EventType == BorrowedItemReminder.OverdueEventType).Payload.Should().BeOfType<BorrowedItemReminder>().Which;
        late.BorrowedItemId.Should().Be(overdue.Id);
        late.DaysOverdue.Should().Be(1);
        late.OfferedByUserId.Should().Be(_volunteer);
    }

    [Fact]
    public async Task Lists_show_offers_to_officers_and_overdue_items()
    {
        await Offer().HandleAsync(new OfferItemForLoanCommand(Input()));
        await IssuedItem(daysUntilReturn: 1);

        var offers = await new ListLoanOffersQueryHandler(Access(Officer()), _store).HandleAsync(1, 20, null);
        offers.TotalItems.Should().Be(1);

        var leader = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Leader, Parish);
        var borrowed = await new ListBorrowedItemsQueryHandler(Access(leader), _store).HandleAsync(new BorrowedItemFilter(1, 20, null, null, null, null));
        borrowed.TotalItems.Should().Be(1);
        (await new ListBorrowedItemsQueryHandler(Access(leader), _store).HandleAsync(new BorrowedItemFilter(1, 20, null, null, null, true))).TotalItems.Should().Be(0);

        (await new ListOverdueBorrowedItemsQueryHandler(Access(Officer()), _store).HandleAsync(null, 2)).Should().ContainSingle();
        (await new ListOverdueBorrowedItemsQueryHandler(Access(Officer()), _store).HandleAsync(_today.AddDays(5), 0)).Single().Overdue.Should().BeTrue();
    }
}
