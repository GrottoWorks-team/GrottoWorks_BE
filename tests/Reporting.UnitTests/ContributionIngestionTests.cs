using System.Text.Json;
using BuildingBlocks.Security;
using Reporting.Application.Ingestion;
using Reporting.Application.Recognition;
using Reporting.Contracts.Events;
using Reporting.Domain;
using Reporting.Domain.Recognition;
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

    private IntegrationEventHandler Handler => new(_store, new ContributionRecorder(_store, _catalog, _publisher), new Reporting.Application.ReadModels.ReadModelProjector(_readModels));

    private RecognitionRule Rule(string typeCode, int points, DateOnly? from = null, DateOnly? to = null, string? name = null)
    {
        var rule = RecognitionRule.Create(_catalog.Type(typeCode).ContributionTypeId, name ?? typeCode, points, null,
            from ?? new DateOnly(2026, 12, 1), to);
        _catalog.Rules.Add(rule);
        return rule;
    }

    private static IncomingEvent Event<T>(string type, T payload, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), type, 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(payload, Json));

    private TaskCompletionReviewed Review(string decision, params Guid[] volunteers) =>
        new(Guid.NewGuid(), _workAreaId, _seasonId, _parishId, "Treo đèn", "WORK", decision, volunteers, Guid.NewGuid(), Dec10);

    private ContributionReceiptConfirmed Receipt(Guid donationId, Guid? donorUserId, string type = "MONEY", bool first = true) =>
        new(donationId, Guid.NewGuid(), Guid.NewGuid(), donorUserId, _parishId, _seasonId, null, type, type == "MONEY" ? 500_000m : null, "VND",
            type == "MONEY" ? [] : [new ContributionReceiptLine(Guid.NewGuid(), Guid.NewGuid(), 10, 2, 8)], false, first, Guid.NewGuid(), Dec10);

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

        AttendanceHoursApproved Hours(DateTimeOffset at) =>
            new(Guid.NewGuid(), Guid.NewGuid(), _workAreaId, _seasonId, _parishId, volunteer, 5, Guid.NewGuid(), at);

        await Handler.HandleAsync(Event(AttendanceHoursApproved.EventType, Hours(Dec10)));
        // 2026-12-10 17:30 UTC is 2026-12-11 in Vietnam: version 2.
        await Handler.HandleAsync(Event(AttendanceHoursApproved.EventType, Hours(new DateTimeOffset(2026, 12, 10, 17, 30, 0, TimeSpan.Zero))));

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
                new FakeCurrentUser(user, role, _parishId));

        var mine = await Query(b, GrottoWorksRoles.Volunteer).GetMineAsync(_seasonId);
        mine.TotalPoints.Should().Be(15);
        mine.Contributions.Should().Be(2);
        mine.ByType.Select(t => (t.ContributionTypeCode, t.Points)).Should().Equal(
            (ContributionType.MonetarySupport, 5), (ContributionType.TaskCompletion, 10));
        mine.Entries.Should().HaveCount(2).And.OnlyContain(e => e.RuleVersion == 1);

        var season = await Query(Guid.NewGuid(), GrottoWorksRoles.Parish).GetSeasonAsync(_seasonId);
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

        var season = await new RecognitionQueryHandler(_store, _catalog, _scopes, new Reporting.Application.Scope.WorkAreaAccess(_scopes, parish), parish)
            .GetSeasonAsync(_seasonId);

        season.Ranking.Select(r => (r.Rank, r.TotalPoints)).Should().Equal((1, 20), (1, 20), (3, 10));
    }
}
