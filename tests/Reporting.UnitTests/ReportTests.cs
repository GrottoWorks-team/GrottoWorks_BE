using System.Text.Json;
using BuildingBlocks.Security;
using Readiness.Contracts.Events;
using Reporting.Application.Ingestion;
using Reporting.Application.ReadModels;
using Reporting.Application.Recognition;
using Reporting.Application.Reports;
using Reporting.Application.Scope;
using Reporting.Domain;
using Reporting.Domain.Scope;
using Resource.Contracts.Events;

namespace Reporting.UnitTests;

public sealed class ReportTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Dec10 = new(2026, 12, 10, 3, 0, 0, TimeSpan.Zero);

    private readonly Guid _parishId = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();
    private readonly Guid _leaderId = Guid.NewGuid();
    private readonly Guid _officerId = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeReadModelStore _readModels = new();
    private readonly FakeContributionStore _contributions = new();
    private readonly FakeRecognitionCatalogStore _catalog = new();
    private readonly WorkAreaScope _grotto;
    private readonly WorkAreaScope _street;

    public ReportTests()
    {
        _grotto = _scopes.Seed(_parishId, _seasonId, _leaderId, _officerId, "Hang đá");
        _street = _scopes.Seed(_parishId, _seasonId, Guid.NewGuid(), Guid.NewGuid(), "Đường kiệu");
    }

    private IntegrationEventHandler Ingest =>
        new(_contributions, new ContributionRecorder(_contributions, _catalog, new FakeIntegrationEventPublisher()), new ReadModelProjector(_readModels));

    private Task Send<T>(string type, T payload) =>
        Ingest.HandleAsync(new IncomingEvent(Guid.NewGuid(), type, 1, null, JsonSerializer.SerializeToElement(payload, Json)));

    private FakeCurrentUser User(string role, Guid? id = null) => new(id ?? Guid.NewGuid(), role, _parishId);

    private ReportQueryHandler Reports(FakeCurrentUser user) =>
        new(new ReportScopeResolver(_scopes, new WorkAreaAccess(_scopes, user), user), _readModels);

    private DashboardQueryHandler Dashboards(FakeCurrentUser user) =>
        new(new ReportScopeResolver(_scopes, new WorkAreaAccess(_scopes, user), user), new WorkAreaAccess(_scopes, user), _scopes, _readModels, user);

    private async Task SeedEventsAsync()
    {
        var cable = Guid.NewGuid();
        await Send(MaterialShortageChanged.EventType, new MaterialShortageChanged(_grotto.WorkAreaId, _seasonId, _parishId, cable, 0, 60, 100, 40, Dec10,
            "CAB-01", "Dây điện", "m"));
        await Send(MaterialShortageChanged.EventType, new MaterialShortageChanged(_street.WorkAreaId, _seasonId, _parishId, Guid.NewGuid(), 0, 5, 5, 0, Dec10,
            "LED-01", "Đèn LED", "cái"));
        var request = Guid.NewGuid();
        await Send(PurchaseRequestSubmitted.EventType, new PurchaseRequestSubmitted(request, _grotto.WorkAreaId, _seasonId, _parishId, _officerId, _leaderId,
            "Thiếu dây", 900_000, "VND", [], Dec10));
        await Send(PurchaseRequestDecided.EventType, new PurchaseRequestDecided(request, _grotto.WorkAreaId, _seasonId, _parishId, _officerId, "APPROVED",
            _leaderId, null, 900_000, "VND", [], Dec10.AddHours(1)));
        await Send(PurchaseDeliveryConfirmed.EventType, new PurchaseDeliveryConfirmed(Guid.NewGuid(), request, Guid.NewGuid(), _grotto.WorkAreaId, _seasonId,
            _parishId, Guid.NewGuid(), _officerId, 850_000, "VND", "PARTIALLY_RECEIVED", [new PurchaseDeliveryLine(cable, 30, 28, 2)], Dec10.AddDays(1)));
        await Send(PurchaseRequestSubmitted.EventType, new PurchaseRequestSubmitted(Guid.NewGuid(), _street.WorkAreaId, _seasonId, _parishId, Guid.NewGuid(),
            null, "Đèn", 200_000, "VND", [], Dec10));
        var pledge = Guid.NewGuid();
        await Send(ContributionPledged.EventType, new ContributionPledged(pledge, Guid.NewGuid(), Guid.NewGuid(), _parishId, _seasonId, _grotto.WorkAreaId,
            _officerId, "MATERIAL", null, "VND", [new ContributionPledgeLine(Guid.NewGuid(), cable, 20)], Dec10));
        await Send(ContributionReceiptConfirmed.EventType, new ContributionReceiptConfirmed(pledge, Guid.NewGuid(), Guid.NewGuid(), null, _parishId, _seasonId,
            _grotto.WorkAreaId, "MATERIAL", null, "VND", [new ContributionReceiptLine(Guid.NewGuid(), cable, 12, 2, 10)], false, true, _officerId,
            Dec10.AddDays(2)));
        await Send(ContributionReceiptConfirmed.EventType, new ContributionReceiptConfirmed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, _parishId,
            _seasonId, null, "MONEY", 1_000_000, "VND", [], true, true, Guid.NewGuid(), Dec10));
        var attendance = Guid.NewGuid();
        var volunteer = Guid.NewGuid();
        await Send(AttendanceHoursApproved.EventType, new AttendanceHoursApproved(attendance, Guid.NewGuid(), _grotto.WorkAreaId, _seasonId, _parishId,
            volunteer, 4, _leaderId, Dec10));
        await Send(AttendanceHoursApproved.EventType, new AttendanceHoursApproved(attendance, Guid.NewGuid(), _grotto.WorkAreaId, _seasonId, _parishId,
            volunteer, 3.5m, _leaderId, Dec10.AddHours(2)));
        await Send(AttendanceHoursApproved.EventType, new AttendanceHoursApproved(Guid.NewGuid(), Guid.NewGuid(), _grotto.WorkAreaId, _seasonId, _parishId,
            volunteer, 2, _leaderId, Dec10.AddDays(5)));
        var checklist = Guid.NewGuid();
        await Send(ReadinessItemFailed.EventType, new ReadinessItemFailed(checklist, Guid.NewGuid(), _grotto.WorkAreaId, _seasonId, _parishId, _leaderId,
            "Điện an toàn", true, "Dây hở", _leaderId, Dec10));
        await Send(ReadinessAreaReady.EventType, new ReadinessAreaReady(checklist, _grotto.WorkAreaId, _seasonId, _parishId, _leaderId, Dec10.AddDays(3)));
        await Send(ReadinessAreaReopened.EventType, new ReadinessAreaReopened(checklist, _grotto.WorkAreaId, _seasonId, _parishId, "Mưa", _leaderId,
            Dec10.AddDays(4)));

        _readModels.Tasks.Add(TaskProjection.FromSnapshot(new TaskSnapshot(Guid.NewGuid(), _grotto.WorkAreaId, "Treo đèn", ProjectedTaskType.Work,
            ProjectedTaskStatus.Completed, null, [volunteer], 1, Dec10), Dec10));
        _readModels.Tasks.Add(TaskProjection.FromSnapshot(new TaskSnapshot(Guid.NewGuid(), _grotto.WorkAreaId, "Dựng hang", ProjectedTaskType.Work,
            ProjectedTaskStatus.InProgress, null, [], 1, DateTimeOffset.UtcNow.AddDays(-1)), Dec10));
        _readModels.Tasks.Add(TaskProjection.FromSnapshot(new TaskSnapshot(Guid.NewGuid(), _street.WorkAreaId, "Cắm cờ", ProjectedTaskType.Work,
            ProjectedTaskStatus.Open, null, [], 1, DateTimeOffset.UtcNow.AddDays(5)), Dec10));
    }

    [Fact]
    public async Task Events_feed_the_read_models()
    {
        await SeedEventsAsync();

        var cable = _readModels.Lines.Single(line => line.MaterialCode == "CAB-01");
        (cable.Required, cable.ConfirmedAvailable, cable.Shortage).Should().Be((100m, 40m, 60m));
        (cable.Pledged, cable.DonatedUsable, cable.DonatedUnusable, cable.PurchasedUsable, cable.PurchasedUnusable).Should().Be((20m, 10m, 2m, 28m, 2m));
        var request = _readModels.Requests.Single(r => r.WorkAreaId == _grotto.WorkAreaId);
        (request.Status, request.EstimatedTotal, request.ActualCost, request.Deliveries).Should().Be(("PARTIALLY_RECEIVED", 900_000m, 850_000m, 1));
        _readModels.Donations.Select(d => d.Status).Should().BeEquivalentTo(["PARTIALLY_RECEIVED", "RECEIVED"]);
        _readModels.Attendance.Select(a => a.ApprovedHours).Should().BeEquivalentTo([3.5m, 2m]);
        var readiness = _readModels.Readiness.Single();
        (readiness.Status, readiness.ReopenCount, readiness.FailedMandatoryItems).Should().Be(("REOPENED", 1, 0));
        _readModels.History.Select(h => h.Kind).Should().Equal("ITEM_FAILED", "AREA_READY", "AREA_REOPENED");
    }

    [Fact]
    public async Task Parish_report_covers_every_area_and_states_its_context()
    {
        await SeedEventsAsync();

        var report = await Reports(User(GrottoWorksRoles.Parish)).BuildAsync(ReportType.Materials, new ReportQuery(_seasonId));

        report.ParishId.Should().Be(_parishId);
        report.SeasonId.Should().Be(_seasonId);
        report.Scope.Should().Be(ReportScopeResolver.ParishScope);
        report.Rows.Should().HaveCount(2);
        report.Rows[0]["workArea"].Should().Be("Đường kiệu");
        report.Totals["linesWithShortage"].Should().Be(2);
        report.Notes.Should().NotBeEmpty();
        report.Columns.Select(c => c.Key).Should().Contain(report.Rows[0].Keys);
    }

    [Fact]
    public async Task Leader_and_officer_only_see_their_own_areas()
    {
        await SeedEventsAsync();

        var leader = await Reports(User(GrottoWorksRoles.Leader, _leaderId)).BuildAsync(ReportType.Purchases, new ReportQuery(_seasonId));
        leader.Scope.Should().Be(ReportScopeResolver.LedScope);
        leader.Rows.Should().ContainSingle().Which["workArea"].Should().Be("Hang đá");

        var officer = await Reports(User(GrottoWorksRoles.MaterialOfficer, _officerId)).BuildAsync(ReportType.Contributions, new ReportQuery(_seasonId));
        officer.Rows.Should().ContainSingle("the money donation without a work area is season-level, parish only")
            .Which["donationType"].Should().Be("MATERIAL");

        var parish = await Reports(User(GrottoWorksRoles.Parish)).BuildAsync(ReportType.Contributions, new ReportQuery(_seasonId));
        parish.Rows.Should().HaveCount(2);
        parish.Totals["receivedMoney"].Should().Be(1_000_000m);

        var outside = () => Reports(User(GrottoWorksRoles.Leader, _leaderId)).BuildAsync(ReportType.Tasks, new ReportQuery(_seasonId, _street.WorkAreaId));
        (await outside.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.NotFound);
    }

    [Fact]
    public async Task Roles_and_parish_are_enforced()
    {
        var denied = () => Reports(User(GrottoWorksRoles.Leader, _leaderId)).BuildAsync(ReportType.Contributions, new ReportQuery(_seasonId));
        (await denied.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("REPORT_ACCESS_DENIED");

        var foreign = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Parish, Guid.NewGuid());
        var other = () => Reports(foreign).BuildAsync(ReportType.Tasks, new ReportQuery(_seasonId));
        (await other.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SEASON_NOT_FOUND");
    }

    [Fact]
    public async Task Attendance_report_sums_approved_hours_in_the_date_range()
    {
        await SeedEventsAsync();
        var parish = Reports(User(GrottoWorksRoles.Parish));

        var all = await parish.BuildAsync(ReportType.Attendance, new ReportQuery(_seasonId));
        all.Totals["approvedHours"].Should().Be(5.5m);
        all.Rows.Should().ContainSingle().Which["sessions"].Should().Be(2);

        var firstDays = await parish.BuildAsync(ReportType.Attendance, new ReportQuery(_seasonId, From: new DateOnly(2026, 12, 10), To: new DateOnly(2026, 12, 12)));
        firstDays.Totals["approvedHours"].Should().Be(3.5m);
        firstDays.Filters.From.Should().Be(new DateOnly(2026, 12, 10));
    }

    [Fact]
    public async Task Task_and_readiness_reports()
    {
        await SeedEventsAsync();
        var parish = Reports(User(GrottoWorksRoles.Parish));

        var tasks = await parish.BuildAsync(ReportType.Tasks, new ReportQuery(_seasonId));
        tasks.Totals["tasks"].Should().Be(3);
        tasks.Totals["overdue"].Should().Be(1);
        tasks.Totals["unassigned"].Should().Be(2);

        var readiness = await parish.BuildAsync(ReportType.Readiness, new ReportQuery(_seasonId));
        readiness.Rows.Count(row => (string?)row["kind"] == "SUMMARY").Should().Be(2);
        readiness.Rows.Should().Contain(row => (string?)row["kind"] == "AREA_REOPENED" && (string?)row["note"] == "Mưa");
        readiness.Rows.Should().Contain(row => (string?)row["status"] == "NO_RESULT");
    }

    [Fact]
    public async Task Season_dashboard_summarises_the_parish()
    {
        await SeedEventsAsync();

        var dashboard = await Dashboards(User(GrottoWorksRoles.Parish)).SeasonAsync(_seasonId);

        var summary = dashboard.Summary;
        summary.WorkAreas.Should().Be(2);
        summary.Tasks.Total.Should().Be(3);
        summary.Tasks.Completed.Should().Be(1);
        summary.Tasks.ProgressPercent.Should().Be(33.3m);
        summary.Tasks.Overdue.Should().Be(1);
        summary.Materials.LinesWithShortage.Should().Be(2);
        summary.Purchases.PendingApproval.Should().Be(1);
        summary.Purchases.InProgress.Should().Be(1);
        summary.Purchases.ActualCost.Should().Be(850_000m);
        summary.Donations.OpenPledges.Should().Be(1);
        summary.Donations.ReceivedMoney.Should().Be(1_000_000m);
        summary.Volunteers.ApprovedHours.Should().Be(5.5m);
        summary.Volunteers.ParticipationPercent.Should().Be(100m);
        summary.Readiness.Reopened.Should().Be(1);
        summary.Readiness.WithoutResult.Should().Be(1);
        summary.NotAvailable.Should().NotBeEmpty();
        dashboard.WorkAreas.Single(area => area.Name == "Hang đá").ProgressPercent.Should().Be(50m);
    }

    [Fact]
    public async Task Work_area_and_material_dashboards_follow_the_role_scope()
    {
        await SeedEventsAsync();

        var leader = await Dashboards(User(GrottoWorksRoles.Leader, _leaderId)).WorkAreaAsync(_grotto.WorkAreaId);
        leader.Summary.Tasks.Total.Should().Be(2);
        leader.Summary.Donations.ReceivedMoney.Should().Be(0, "season-level money is not part of an area");

        var notMine = () => Dashboards(User(GrottoWorksRoles.Leader, _leaderId)).WorkAreaAsync(_street.WorkAreaId);
        (await notMine.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.NotFound);

        var officer = await Dashboards(User(GrottoWorksRoles.MaterialOfficer, _officerId)).MaterialsAsync(null, _seasonId);
        officer.WorkAreaIds.Should().Equal(_grotto.WorkAreaId);
        officer.Lines.Should().ContainSingle().Which.Shortage.Should().Be(60);
        officer.Purchases.InProgress.Should().Be(1);
    }
}
