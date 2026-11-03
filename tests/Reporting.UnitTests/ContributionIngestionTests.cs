using System.Text.Json;
using BuildingBlocks.Security;
using Reporting.Application.Ingestion;
using Reporting.Application.Recognition;
using Reporting.Application.Users;
using Reporting.Contracts.Events;
using Reporting.Domain;
using Reporting.Domain.Recognition;
using Reporting.Domain.Scope;
using Resource.Contracts.Events;

namespace Reporting.UnitTests;

public sealed class ContributionIngestionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Dec10 = new(2026, 12, 10, 3, 0, 0, TimeSpan.Zero);

    private readonly Guid _parishId = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();
    private readonly Guid _workAreaId = Guid.NewGuid();
    private readonly FakeRecognitionCatalogStore _catalog = new();
    private readonly FakeContributionStore _store = new();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeIntegrationEventPublisher _publisher = new();

    private readonly FakeReadModelStore _readModels = new();
    private readonly FakeUserDirectory _users = new();

    private IntegrationEventHandler Handler =>
        new(_store, new ContributionRecorder(_store, _catalog, _publisher), new Reporting.Application.ReadModels.ReadModelProjector(_readModels), _readModels);

    private RecognitionRule Rule(string typeCode, int points, DateOnly? from = null, DateOnly? to = null, string? name = null)
    {
        var rule = RecognitionRule.Create(_catalog.Type(typeCode).ContributionTypeId, name ?? typeCode, points, null,
            from ?? new DateOnly(2026, 12, 1), to);
        _catalog.Rules.Add(rule);
        return rule;
    }

    private static IncomingEvent Event<T>(string type, T payload, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), type, 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(payload, Json));

    /// <summary>attendance.hours.approved exactly as TaskExecution's outbox publishes it (no work area in the payload).</summary>
    private IncomingEvent Hours(Guid volunteer, DateTimeOffset at, Guid? taskId = null, int minutes = 300) =>
        new(Guid.NewGuid(), TaskHoursApproved.EventType, 1, at,
            JsonSerializer.SerializeToElement(new { attendanceId = Guid.NewGuid(), assignmentId = Guid.NewGuid(), volunteerUserId = volunteer,
                checkInAt = at.AddMinutes(-minutes), checkOutAt = at, approvedMinutes = minutes, previousApprovedMinutes = 0 }, Json),
            _parishId, _seasonId, taskId ?? Guid.NewGuid(), 4);

    private TaskCompletionReviewed Review(string decision, params Guid[] volunteers) =>
        new(Guid.NewGuid(), _workAreaId, _seasonId, _parishId, "Treo đèn", "WORK", decision, volunteers, Guid.NewGuid(), Dec10);

    private ContributionReceiptConfirmed Receipt(Guid donationId, Guid? donorUserId, string type = "MONEY", bool first = true) =>
        new(donationId, Guid.NewGuid(), Guid.NewGuid(), donorUserId, _parishId, _seasonId, null, type, type == "MONEY" ? 500_000m : null, "VND",
            type == "MONEY" ? [] : [new ContributionReceiptLine(Guid.NewGuid(), Guid.NewGuid(), 10, 2, 8)], false, first, Guid.NewGuid(), Dec10);

    private IncomingEvent LegacyReview(Guid taskId, Guid leader, Guid volunteer, string decision = "APPROVED") =>
        new(Guid.NewGuid(), TaskCompletionReviewed.EventType, 1, Dec10,
            JsonSerializer.SerializeToElement(new { taskId, workAreaId = _workAreaId, approved = decision == "APPROVED", decision,
                status = decision == "APPROVED" ? "COMPLETED" : "REVISION_REQUIRED", leaderUserId = leader,
                recipientUserIds = new[] { leader, volunteer } }, Json), _parishId, _seasonId, taskId, 4);

    private void ProjectTask(Guid taskId, ProjectedTaskType type, params Guid[] volunteers) =>
        _readModels.Tasks.Add(TaskProjection.FromSnapshot(new TaskSnapshot(taskId, _workAreaId, "Treo đèn", type,
            ProjectedTaskStatus.Completed, type == ProjectedTaskType.Purchase ? Guid.NewGuid() : null, volunteers, 4), Dec10));

    [Fact]
    public async Task Legacy_work_review_uses_projection_participants_and_envelope_context()
    {
        Rule(ContributionType.TaskCompletion, 10);
        var (taskId, leader, volunteer, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        ProjectTask(taskId, ProjectedTaskType.Work, volunteer, other);
        var incoming = LegacyReview(taskId, leader, volunteer);

        var result = await Handler.HandleAsync(incoming);

        result.Outcome.Should().Be(IngestionOutcome.Applied);
        result.ContributionsRecorded.Should().Be(2);
        result.PointsAwarded.Should().Be(2);
        _store.Contributions.Select(c => c.ContributedByUserId).Should().BeEquivalentTo(new[] { volunteer, other });
        _store.Contributions.Should().OnlyContain(c => c.ParishId == _parishId && c.SeasonId == _seasonId &&
            c.WorkAreaId == _workAreaId && c.ReferenceId == taskId && c.ContributionDate == Dec10 && c.Description == "Hoàn thành task Treo đèn");
        _store.Points.Should().NotContain(p => p.AwardedToUserId == leader);
        (await Handler.HandleAsync(incoming)).Outcome.Should().Be(IngestionOutcome.Duplicate);
        _store.Processed.Should().ContainSingle();
    }

    [Fact]
    public async Task Legacy_purchase_review_earns_credit_only_from_resource_delivery()
    {
        Rule(ContributionType.TaskCompletion, 10);
        Rule(ContributionType.PurchaseAssistance, 5);
        var (taskId, buyer) = (Guid.NewGuid(), Guid.NewGuid());
        ProjectTask(taskId, ProjectedTaskType.Purchase, buyer);

        var result = await Handler.HandleAsync(LegacyReview(taskId, Guid.NewGuid(), buyer));

        result.Outcome.Should().Be(IngestionOutcome.Applied);
        result.ContributionsRecorded.Should().Be(0);
        result.PointsAwarded.Should().Be(0);
        _store.Contributions.Should().BeEmpty();
        _store.Points.Should().BeEmpty();
        var delivery = new PurchaseDeliveryConfirmed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _workAreaId, _seasonId, _parishId,
            buyer, Guid.NewGuid(), 120_000m, "VND", "COMPLETED", [], Dec10);
        await Handler.HandleAsync(Event(PurchaseDeliveryConfirmed.EventType, delivery));

        _store.Contributions.Should().ContainSingle().Which.ContributionTypeId.Should().Be(_catalog.Type(ContributionType.PurchaseAssistance).ContributionTypeId);
        _store.Points.Should().ContainSingle().Which.AwardedToUserId.Should().Be(buyer);
    }

    [Fact]
    public async Task Legacy_review_without_projection_is_retryable_and_does_not_enter_inbox()
    {
        Rule(ContributionType.TaskCompletion, 10);
        var (taskId, volunteer) = (Guid.NewGuid(), Guid.NewGuid());
        var incoming = LegacyReview(taskId, Guid.NewGuid(), volunteer);
        var act = () => Handler.HandleAsync(incoming);

        var exception = (await act.Should().ThrowAsync<DomainRuleException>()).Which;
        exception.Code.Should().Be("EVENT_CONTEXT_UNAVAILABLE");
        exception.Kind.Should().Be(DomainErrorKind.ServiceUnavailable);
        exception.Message.Should().Contain(taskId.ToString());
        _store.Processed.Should().BeEmpty();
        _store.Contributions.Should().BeEmpty();
        _store.Points.Should().BeEmpty();
        _publisher.Events.Should().BeEmpty();

        ProjectTask(taskId, ProjectedTaskType.Work, volunteer);
        (await Handler.HandleAsync(incoming)).ContributionsRecorded.Should().Be(1);
        _store.Processed.Should().ContainSingle();
    }

    [Fact]
    public async Task Legacy_revision_required_review_preserves_no_credit_without_projection()
    {
        var result = await Handler.HandleAsync(LegacyReview(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "REVISION_REQUIRED"));

        result.Outcome.Should().Be(IngestionOutcome.Applied);
        result.ContributionsRecorded.Should().Be(0);
        _store.Processed.Should().ContainSingle();
    }

    [Theory]
    [InlineData("taskId")]
    [InlineData("workAreaId")]
    [InlineData("decision")]
    [InlineData("taskType")]
    [InlineData("parishId")]
    [InlineData("seasonId")]
    [InlineData("reviewedAt")]
    [InlineData("volunteerUserIds")]
    public async Task Invalid_full_review_is_rejected_before_inbox(string field)
    {
        var payload = JsonSerializer.SerializeToNode(Review("APPROVED", Guid.NewGuid()), Json)!.AsObject();
        payload[field] = field switch
        {
            "taskId" or "workAreaId" or "parishId" or "seasonId" => JsonSerializer.SerializeToNode(Guid.Empty),
            "reviewedAt" => JsonSerializer.SerializeToNode(default(DateTimeOffset)),
            "volunteerUserIds" => JsonSerializer.SerializeToNode(new[] { Guid.Empty }),
            _ => JsonSerializer.SerializeToNode(field == "decision" ? "" : "UNKNOWN")
        };
        var incoming = new IncomingEvent(Guid.NewGuid(), TaskCompletionReviewed.EventType, 1, null, JsonSerializer.SerializeToElement(payload, Json));

        var act = () => Handler.HandleAsync(incoming);

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("EVENT_PAYLOAD_INVALID");
        _store.Processed.Should().BeEmpty();
        _store.Contributions.Should().BeEmpty();
    }

    [Fact]
    public async Task Approved_task_credits_each_volunteer_once_with_points_and_events()
    {
        var rule = Rule(ContributionType.TaskCompletion, 10);
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());

        var result = await Handler.HandleAsync(Event(TaskCompletionReviewed.EventType, Review("APPROVED", a, b, a)));

        result.Outcome.Should().Be(IngestionOutcome.Applied);
        result.ContributionsRecorded.Should().Be(2);
        result.PointsAwarded.Should().Be(2);
        _store.Contributions.Should().OnlyContain(c => c.ReferenceType == "TASK" && c.DonationId == null && c.ParishId == _parishId);
        _store.Points.Should().OnlyContain(p => p.RecognitionRuleId == rule.RecognitionRuleId && p.AwardedPoints == 10);
        _publisher.Events.Should().HaveCount(2).And.OnlyContain(e => e.EventType == RecognitionEntryCreated.EventType);
        _store.Processed.Should().ContainSingle();
    }

    [Fact]
    public async Task Redelivered_or_repeated_events_are_never_counted_twice()
    {
        Rule(ContributionType.TaskCompletion, 10);
        var review = Review("APPROVED", Guid.NewGuid());
        var incoming = Event(TaskCompletionReviewed.EventType, review);
        await Handler.HandleAsync(incoming);

        (await Handler.HandleAsync(incoming)).Outcome.Should().Be(IngestionOutcome.Duplicate);
        var again = await Handler.HandleAsync(Event(TaskCompletionReviewed.EventType, review));

        again.Outcome.Should().Be(IngestionOutcome.Applied);
        again.ContributionsRecorded.Should().Be(0);
        again.PointsAwarded.Should().Be(0);
        _store.Contributions.Should().ContainSingle();
        _store.Points.Should().ContainSingle();
    }

    [Theory]
    [InlineData("REVISION_REQUIRED", "WORK")]
    [InlineData("APPROVED", "PURCHASE")]
    public async Task Only_approved_non_purchase_reviews_count(string decision, string taskType)
    {
        Rule(ContributionType.TaskCompletion, 10);
        var review = Review(decision, Guid.NewGuid()) with { TaskType = taskType };

        var result = await Handler.HandleAsync(Event(TaskCompletionReviewed.EventType, review));

        result.Outcome.Should().Be(IngestionOutcome.Applied);
        _store.Contributions.Should().BeEmpty();
    }

    [Fact]
    public async Task Donation_is_one_contribution_and_points_go_to_the_linked_account_once()
    {
        Rule(ContributionType.MonetarySupport, 5);
        var donor = Guid.NewGuid();
        var donationId = Guid.NewGuid();

        await Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, Receipt(donationId, donor)));
        await Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, Receipt(donationId, donor, first: false)));

        var contribution = _store.Contributions.Should().ContainSingle().Subject;
        contribution.DonationId.Should().Be(donationId);
        contribution.ContributedByUserId.Should().BeNull();
        contribution.DonorUserId.Should().Be(donor);
        contribution.ContributionTypeId.Should().Be(_catalog.Type(ContributionType.MonetarySupport).ContributionTypeId);
        _store.Points.Should().ContainSingle().Which.AwardedToUserId.Should().Be(donor);
    }

    [Fact]
    public async Task External_donor_gets_no_points_until_the_donor_is_linked()
    {
        Rule(ContributionType.MaterialDonation, 7);
        var donationId = Guid.NewGuid();

        await Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, Receipt(donationId, null, "MATERIAL")));
        _store.Contributions.Should().ContainSingle().Which.ContributionTypeId
            .Should().Be(_catalog.Type(ContributionType.MaterialDonation).ContributionTypeId);
        _store.Points.Should().BeEmpty();

        var linked = Guid.NewGuid();
        await Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, Receipt(donationId, linked, "MATERIAL", first: false)));

        _store.Points.Should().ContainSingle().Which.AwardedToUserId.Should().Be(linked);
    }

    [Fact]
    public async Task Adjustment_updates_the_donation_summary_without_new_points()
    {
        Rule(ContributionType.MonetarySupport, 5);
        var donationId = Guid.NewGuid();
        var donor = Guid.NewGuid();
        await Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, Receipt(donationId, donor)));

        var adjusted = new ContributionDonationAdjusted(donationId, Guid.NewGuid(), Guid.NewGuid(), donor, _parishId, _seasonId, null, "MONEY", -100_000m,
            "VND", [], "Đếm sai", Guid.NewGuid(), Dec10.AddDays(1));
        await Handler.HandleAsync(Event(ContributionDonationAdjusted.EventType, adjusted));

        _store.Contributions.Should().ContainSingle().Which.Description.Should().Contain("Đếm sai");
        _store.Points.Should().ContainSingle();
    }

    [Fact]
    public async Task The_rule_version_effective_on_the_contribution_date_applies()
    {
        var v1 = Rule(ContributionType.VolunteerHours, 20, new DateOnly(2026, 12, 1), name: "Giờ làm");
        var v2 = v1.NextVersion(25, null, new DateOnly(2026, 12, 11), null);
        _catalog.Rules.Add(v2);
        var volunteer = Guid.NewGuid();

        await Handler.HandleAsync(Hours(volunteer, Dec10));
        // 2026-12-10 17:30 UTC is 2026-12-11 in Vietnam: version 2.
        await Handler.HandleAsync(Hours(volunteer, new DateTimeOffset(2026, 12, 10, 17, 30, 0, TimeSpan.Zero)));

        _store.Points.Select(p => p.AwardedPoints).Should().Equal(20, 25);
        _store.Points[0].Reason.Should().Be("Giờ làm (v1)");
    }

    [Fact]
    public async Task Inactive_type_records_the_contribution_without_points()
    {
        Rule(ContributionType.PurchaseAssistance, 10);
        _catalog.Type(ContributionType.PurchaseAssistance).Update(null, null, ContributionTypeStatus.Inactive);
        var purchase = new PurchaseDeliveryConfirmed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _workAreaId, _seasonId, _parishId, Guid.NewGuid(),
            Guid.NewGuid(), 120_000m, "VND", "COMPLETED", [], Dec10);

        await Handler.HandleAsync(Event(PurchaseDeliveryConfirmed.EventType, purchase));

        _store.Contributions.Should().ContainSingle().Which.ContributedByUserId.Should().Be(purchase.BuyerUserId);
        _store.Points.Should().BeEmpty();
    }

    [Fact]
    public async Task Support_member_completion_counts_for_the_volunteer()
    {
        Rule(ContributionType.SupportAssistance, 25);
        var support = new SupportMemberCompleted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _workAreaId, _seasonId, _parishId, Dec10);

        await Handler.HandleAsync(Event(SupportMemberCompleted.EventType, support));

        _store.Points.Should().ContainSingle().Which.AwardedPoints.Should().Be(25);
    }

    [Fact]
    public async Task Unknown_events_are_acknowledged_and_bad_payloads_rejected()
    {
        var ignored = await Handler.HandleAsync(Event("planning.season.activated", new { seasonId = _seasonId }));
        ignored.Outcome.Should().Be(IngestionOutcome.Ignored);
        _store.Processed.Should().ContainSingle();

        var bad = () => Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, new { donationId = "not-a-guid" }));
        (await bad.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("EVENT_PAYLOAD_INVALID");
    }

    [Fact]
    public async Task Event_for_another_season_than_the_contribution_is_rejected()
    {
        var donationId = Guid.NewGuid();
        await Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, Receipt(donationId, null)));

        var other = Receipt(donationId, null, first: false) with { SeasonId = Guid.NewGuid() };
        var act = () => Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, other));

        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("EVENT_SEASON_MISMATCH");
    }

    [Fact]
    public async Task Recognition_summary_and_season_ranking()
    {
        Rule(ContributionType.TaskCompletion, 10);
        Rule(ContributionType.MonetarySupport, 5);
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Handler.HandleAsync(Event(TaskCompletionReviewed.EventType, Review("APPROVED", a, b)));
        await Handler.HandleAsync(Event(TaskCompletionReviewed.EventType, Review("APPROVED", a)));
        await Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, Receipt(Guid.NewGuid(), b)));
        await Handler.HandleAsync(Event(ContributionReceiptConfirmed.EventType, Receipt(Guid.NewGuid(), null)));
        await Handler.HandleAsync(Event(TaskCompletionReviewed.EventType, Review("APPROVED", c) with { ParishId = Guid.NewGuid() }));

        RecognitionQueryHandler Query(Guid user, string role) =>
            new(_store, _catalog, _scopes, new Reporting.Application.Scope.WorkAreaAccess(_scopes, new FakeCurrentUser(user, role, _parishId)),
                new FakeCurrentUser(user, role, _parishId), _users);

        var mine = await Query(b, GrottoWorksRoles.Volunteer).GetMineAsync(_seasonId);
        mine.TotalPoints.Should().Be(15);
        mine.Contributions.Should().Be(2);
        mine.ByType.Select(t => (t.ContributionTypeCode, t.Points)).Should().Equal(
            (ContributionType.MonetarySupport, 5), (ContributionType.TaskCompletion, 10));
        mine.Entries.Should().HaveCount(2).And.OnlyContain(e => e.RuleVersion == 1);

        _users.Names[a] = "Nguyễn Văn A";
        var season = await Query(Guid.NewGuid(), GrottoWorksRoles.Parish).GetSeasonAsync(_seasonId);
        season.Ranking[0].DisplayName.Should().Be("Nguyễn Văn A");
        season.Ranking[1].DisplayName.Should().BeNull("unknown users keep no name");
        season.Contributions.Should().Be(5);
        season.ExternalDonorContributions.Should().Be(1);
        season.TotalPoints.Should().Be(35);
        season.Ranking.Select(r => (r.Rank, r.TotalPoints)).Should().Equal((1, 20), (2, 15));
        season.Ranking.Should().NotContain(r => r.UserId == c);

        var unknown = () => Query(Guid.NewGuid(), GrottoWorksRoles.Parish).GetSeasonAsync(Guid.NewGuid());
        (await unknown.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.NotFound);
    }

    [Fact]
    public async Task Equal_points_share_a_rank()
    {
        Rule(ContributionType.TaskCompletion, 10);
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Handler.HandleAsync(Event(TaskCompletionReviewed.EventType, Review("APPROVED", a, b, c)));
        await Handler.HandleAsync(Event(TaskCompletionReviewed.EventType, Review("APPROVED", a)));
        await Handler.HandleAsync(Event(TaskCompletionReviewed.EventType, Review("APPROVED", b)));
        var parish = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Parish, _parishId);

        var season = await new RecognitionQueryHandler(_store, _catalog, _scopes, new Reporting.Application.Scope.WorkAreaAccess(_scopes, parish), parish,
                new NoUserDirectory())
            .GetSeasonAsync(_seasonId);

        season.Ranking.Select(r => (r.Rank, r.TotalPoints)).Should().Equal((1, 20), (1, 20), (3, 10));
    }

    [Fact]
    public async Task Task_hours_event_takes_the_work_area_from_the_task_projection()
    {
        var task = Reporting.Domain.Scope.TaskProjection.FromSnapshot(new Reporting.Domain.Scope.TaskSnapshot(Guid.NewGuid(), _workAreaId, "Treo đèn",
            Reporting.Domain.Scope.ProjectedTaskType.Work, Reporting.Domain.Scope.ProjectedTaskStatus.InProgress, null, [], 1), Dec10);
        _readModels.Tasks.Add(task);
        Rule(ContributionType.VolunteerHours, 20);
        var volunteer = Guid.NewGuid();

        await Handler.HandleAsync(Hours(volunteer, Dec10, task.TaskId, 150));
        await Handler.HandleAsync(Hours(volunteer, Dec10, Guid.NewGuid()));

        var known = _readModels.Attendance.Single(a => a.TaskId == task.TaskId);
        (known.WorkAreaId, known.ApprovedHours).Should().Be((_workAreaId, 2.5m));
        _readModels.Attendance.Single(a => a.TaskId != task.TaskId).WorkAreaId.Should().BeNull("unknown task, no work area in the payload");
        _store.Contributions.Should().HaveCount(2).And.OnlyContain(c => c.ParishId == _parishId && c.SeasonId == _seasonId);
        _store.Points.Should().HaveCount(2);
    }

    [Fact]
    public async Task Task_status_and_accepted_assignment_update_the_task_projection()
    {
        var task = Reporting.Domain.Scope.TaskProjection.FromSnapshot(new Reporting.Domain.Scope.TaskSnapshot(Guid.NewGuid(), _workAreaId, "Treo đèn",
            Reporting.Domain.Scope.ProjectedTaskType.Work, Reporting.Domain.Scope.ProjectedTaskStatus.Open, null, [], 1), Dec10);
        _readModels.Tasks.Add(task);
        var volunteer = Guid.NewGuid();

        IncomingEvent TaskEvent(string type, object payload, long version) =>
            new(Guid.NewGuid(), type, 1, Dec10, JsonSerializer.SerializeToElement(payload, Json), _parishId, _seasonId, task.TaskId, version);

        (await Handler.HandleAsync(TaskEvent("task.assignment.responded", new { volunteerUserId = volunteer, decision = "ACCEPTED" }, 2)))
            .Outcome.Should().Be(IngestionOutcome.Applied);
        await Handler.HandleAsync(TaskEvent("task.status.changed", new { status = "IN_PROGRESS" }, 3));
        await Handler.HandleAsync(TaskEvent("task.status.changed", new { status = "OPEN" }, 2));

        task.AssigneeUserIds.Should().Equal(volunteer);
        task.Status.Should().Be(Reporting.Domain.Scope.ProjectedTaskStatus.InProgress);
        (await Handler.HandleAsync(new IncomingEvent(Guid.NewGuid(), "task.status.changed", 1, Dec10,
            JsonSerializer.SerializeToElement(new { status = "DONE" }, Json), _parishId, _seasonId, Guid.NewGuid(), 1)))
            .Outcome.Should().Be(IngestionOutcome.Ignored, "unknown tasks are not projected");
    }

    [Fact]
    public async Task Hours_received_before_the_task_get_its_work_area_once_the_task_is_projected()
    {
        var taskId = Guid.NewGuid();
        await Handler.HandleAsync(Hours(Guid.NewGuid(), Dec10, taskId, 90));
        _readModels.Attendance.Single().WorkAreaId.Should().BeNull();

        var scopes = new FakeScopeStore();
        var area = scopes.Seed(_parishId, _seasonId);
        await new Reporting.Application.Scope.ApplyTaskSnapshotHandler(new FakeTaskStore(), scopes, _readModels).HandleAsync(
            new Reporting.Domain.Scope.TaskSnapshot(taskId, area.WorkAreaId, "Treo đèn", Reporting.Domain.Scope.ProjectedTaskType.Work,
                Reporting.Domain.Scope.ProjectedTaskStatus.InProgress, null, [], 1));

        _readModels.Attendance.Single().WorkAreaId.Should().Be(area.WorkAreaId);
    }

    private sealed class FakeTaskStore : Reporting.Application.Scope.ITaskProjectionStore
    {
        private readonly List<Reporting.Domain.Scope.TaskProjection> _tasks = [];

        public Task<Reporting.Domain.Scope.TaskProjection?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tasks.FirstOrDefault(t => t.TaskId == taskId));

        public void Add(Reporting.Domain.Scope.TaskProjection task) => _tasks.Add(task);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
