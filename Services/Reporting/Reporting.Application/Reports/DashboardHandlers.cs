using BuildingBlocks.Security;
using Reporting.Application.ReadModels;
using Reporting.Application.Scope;
using Reporting.Domain;
using Reporting.Domain.ReadModels;
using Reporting.Domain.Scope;

namespace Reporting.Application.Reports;

public sealed record TaskSummaryDto(
    int Total,
    IReadOnlyDictionary<string, int> ByStatus,
    int Unassigned,
    int Overdue,
    int RevisionRequired,
    int Completed,
    decimal ProgressPercent);

public sealed record MaterialSummaryDto(int Lines, int LinesWithShortage, int PledgedLines);

public sealed record PurchaseSummaryDto(int PendingApproval, int InProgress, int Completed, int Rejected, decimal EstimatedApproved, decimal ActualCost);

public sealed record DonationSummaryDto(int OpenPledges, decimal OpenPledgedMoney, decimal ReceivedMoney, decimal ReceivedUsableQuantity, decimal ReceivedUnusableQuantity);

public sealed record VolunteerSummaryDto(int Assigned, int WithApprovedHours, decimal ApprovedHours, decimal ParticipationPercent);

public sealed record ReadinessSummaryDto(int Ready, int NeedsCorrection, int Reopened, int WithoutResult, int FailedMandatoryItems);

/// <summary>Figures of a set of work areas (BA 12.1). <see cref="NotAvailable"/> lists indicators waiting for TaskExecution events.</summary>
public sealed record AreaSummaryDto(
    int WorkAreas,
    IReadOnlyDictionary<string, int> WorkAreasByStatus,
    TaskSummaryDto Tasks,
    MaterialSummaryDto Materials,
    PurchaseSummaryDto Purchases,
    DonationSummaryDto Donations,
    VolunteerSummaryDto Volunteers,
    ReadinessSummaryDto Readiness,
    IReadOnlyList<string> NotAvailable);

public sealed record CommunityProgressDto(Guid? CommunityId, int WorkAreas, int Tasks, int Completed, decimal ProgressPercent);

public sealed record WorkAreaProgressDto(Guid WorkAreaId, string Name, WorkAreaStatus Status, int Tasks, int Completed, decimal ProgressPercent, string? Readiness);

/// <summary><c>SeasonDashboard</c> (PARISH).</summary>
public sealed record SeasonDashboardDto(
    Guid SeasonId,
    Guid ParishId,
    DateTimeOffset GeneratedAt,
    AreaSummaryDto Summary,
    IReadOnlyList<CommunityProgressDto> Communities,
    IReadOnlyList<WorkAreaProgressDto> WorkAreas);

/// <summary><c>WorkAreaDashboard</c> (LEADER of the area).</summary>
public sealed record WorkAreaDashboardDto(
    Guid WorkAreaId,
    string Name,
    WorkAreaStatus Status,
    Guid SeasonId,
    DateTimeOffset GeneratedAt,
    AreaSummaryDto Summary);

public sealed record MaterialLineDto(
    Guid WorkAreaId,
    string WorkAreaName,
    Guid MaterialId,
    string? MaterialCode,
    string? MaterialName,
    string? Unit,
    decimal Required,
    decimal ConfirmedAvailable,
    decimal Shortage,
    decimal Pledged,
    decimal DonatedUsable,
    decimal PurchasedUsable);

/// <summary><c>MaterialDashboard</c> (MO of the areas): shortages first, purchases waiting, open pledges.</summary>
public sealed record MaterialDashboardDto(
    Guid SeasonId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<Guid> WorkAreaIds,
    IReadOnlyList<MaterialLineDto> Lines,
    PurchaseSummaryDto Purchases,
    DonationSummaryDto Donations);

/// <summary>F-RPT-04: dashboards computed from the read models of the caller's scope (BR-57).</summary>
public sealed class DashboardQueryHandler(
    ReportScopeResolver resolver,
    WorkAreaAccess access,
    IWorkAreaScopeStore scopes,
    IReadModelStore store,
    ICurrentUser currentUser)
{
    private static readonly string[] Pending = ["Support request đang mở (chờ event support.request.*)", "Check-in chờ duyệt (chờ event attendance.request.*)"];

    public async Task<SeasonDashboardDto> SeasonAsync(Guid seasonId, CancellationToken cancellationToken = default)
    {
        var scope = await resolver.ResolveAsync(new ReportQuery(seasonId), cancellationToken);
        var data = await LoadAsync(scope, cancellationToken);
        var summary = Summarise(scope, data);
        var communities = scope.WorkAreas
            .GroupBy(area => area.CommunityId)
            .Select(group =>
            {
                var ids = group.Select(area => area.WorkAreaId).ToHashSet();
                var tasks = data.Tasks.Where(task => ids.Contains(task.WorkAreaId) && task.Status != ProjectedTaskStatus.Cancelled).ToList();
                var completed = tasks.Count(task => task.Status == ProjectedTaskStatus.Completed);
                return new CommunityProgressDto(group.Key, group.Count(), tasks.Count, completed, Percent(completed, tasks.Count));
            })
            .ToList();
        var readiness = data.Readiness.ToDictionary(state => state.WorkAreaId);
        var areas = scope.WorkAreas.Select(area =>
            {
                var tasks = data.Tasks.Where(task => task.WorkAreaId == area.WorkAreaId && task.Status != ProjectedTaskStatus.Cancelled).ToList();
                var completed = tasks.Count(task => task.Status == ProjectedTaskStatus.Completed);
                return new WorkAreaProgressDto(area.WorkAreaId, area.Name, area.Status, tasks.Count, completed, Percent(completed, tasks.Count),
                    readiness.GetValueOrDefault(area.WorkAreaId)?.Status);
            })
            .ToList();
        return new SeasonDashboardDto(seasonId, scope.ParishId, DateTimeOffset.UtcNow, summary, communities, areas);
    }

    public async Task<WorkAreaDashboardDto> WorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default)
    {
        var area = await access.ForReadAsync(workAreaId, cancellationToken);
        var scope = await resolver.ResolveAsync(new ReportQuery(area.SeasonId, workAreaId), cancellationToken);
        var data = await LoadAsync(scope, cancellationToken);
        return new WorkAreaDashboardDto(area.WorkAreaId, area.Name, area.Status, area.SeasonId, DateTimeOffset.UtcNow, Summarise(scope, data));
    }

    /// <summary>The MO's areas of one season — by <paramref name="workAreaId"/>, or every area it manages in <paramref name="seasonId"/>.</summary>
    public async Task<MaterialDashboardDto> MaterialsAsync(Guid? workAreaId, Guid? seasonId, CancellationToken cancellationToken = default)
    {
        Guid season;
        if (workAreaId is { } id)
        {
            season = (await access.ForReadAsync(id, cancellationToken)).SeasonId;
        }
        else
        {
            season = seasonId
                ?? (await scopes.ListByParishAsync(access.RequireParish(), null, cancellationToken))
                    .Where(area => area.MaterialOfficerUserId == currentUser.UserId && area.SeasonStatus != SeasonStatus.Archived)
                    .Select(area => (Guid?)area.SeasonId)
                    .FirstOrDefault()
                ?? throw DomainRuleException.Validation("SEASON_REQUIRED", "Pass workAreaId or seasonId.");
        }

        var scope = await resolver.ResolveAsync(new ReportQuery(season, workAreaId), cancellationToken);
        var data = await LoadAsync(scope, cancellationToken);
        var names = scope.WorkAreas.ToDictionary(area => area.WorkAreaId, area => area.Name);
        var lines = data.Lines
            .OrderByDescending(line => line.Shortage)
            .ThenBy(line => names[line.WorkAreaId], StringComparer.CurrentCulture)
            .ThenBy(line => line.MaterialName, StringComparer.CurrentCulture)
            .Select(line => new MaterialLineDto(line.WorkAreaId, names[line.WorkAreaId], line.MaterialId, line.MaterialCode, line.MaterialName, line.Unit,
                line.Required, line.ConfirmedAvailable, line.Shortage, line.Pledged, line.DonatedUsable, line.PurchasedUsable))
            .ToList();
        return new MaterialDashboardDto(season, DateTimeOffset.UtcNow, scope.Ids, lines, Purchases(data.Requests), Donations(data.Donations));
    }

    private async Task<Data> LoadAsync(ResolvedScope scope, CancellationToken cancellationToken)
    {
        var ids = scope.Ids;
        var areas = ids.ToHashSet();
        var donations = (await store.ListDonationsAsync(scope.ParishId, scope.SeasonId, cancellationToken))
            .Where(donation => donation.WorkAreaId is { } area ? areas.Contains(area) : scope.WholeParish)
            .ToList();
        return new Data(
            await store.ListTasksAsync(ids, cancellationToken),
            await store.ListMaterialLinesAsync(ids, cancellationToken),
            await store.ListPurchaseRequestsAsync(ids, cancellationToken),
            donations,
            await store.ListAttendanceAsync(ids, cancellationToken),
            await store.ListReadinessAsync(ids, cancellationToken));
    }

    private static AreaSummaryDto Summarise(ResolvedScope scope, Data data)
    {
        var now = DateTimeOffset.UtcNow;
        var active = data.Tasks.Where(task => task.Status != ProjectedTaskStatus.Cancelled).ToList();
        var completed = active.Count(task => task.Status == ProjectedTaskStatus.Completed);
        var tasks = new TaskSummaryDto(
            active.Count,
            data.Tasks.GroupBy(task => Upper(task.Status.ToString())).ToDictionary(group => group.Key, group => group.Count()),
            active.Count(task => !task.IsClosed && task.AssigneeUserIds.Length == 0),
            active.Count(task => task.IsOverdue(now)),
            active.Count(task => task.Status == ProjectedTaskStatus.RevisionRequired),
            completed,
            Percent(completed, active.Count));
        var assigned = data.Tasks.SelectMany(task => task.AssigneeUserIds).ToHashSet();
        var withHours = data.Attendance.Select(item => item.VolunteerUserId).ToHashSet();
        var volunteers = new VolunteerSummaryDto(assigned.Count, withHours.Count, data.Attendance.Sum(item => item.ApprovedHours),
            Percent(withHours.Count(assigned.Contains), assigned.Count));
        var readiness = new ReadinessSummaryDto(
            data.Readiness.Count(state => state.Status == "READY"),
            data.Readiness.Count(state => state.Status == "NEEDS_CORRECTION"),
            data.Readiness.Count(state => state.Status == "REOPENED"),
            scope.WorkAreas.Count - data.Readiness.Count,
            data.Readiness.Sum(state => state.FailedMandatoryItems));
        return new AreaSummaryDto(
            scope.WorkAreas.Count,
            scope.WorkAreas.GroupBy(area => Upper(area.Status.ToString())).ToDictionary(group => group.Key, group => group.Count()),
            tasks,
            new MaterialSummaryDto(data.Lines.Count, data.Lines.Count(line => line.Shortage > 0), data.Lines.Count(line => line.Pledged > 0)),
            Purchases(data.Requests),
            Donations(data.Donations),
            volunteers,
            readiness,
            Pending);
    }

    private static PurchaseSummaryDto Purchases(IReadOnlyCollection<PurchaseRequestView> requests)
    {
        var approved = requests.Where(request => request.Status is not ("PENDING_APPROVAL" or "REJECTED" or "")).ToList();
        return new PurchaseSummaryDto(
            requests.Count(request => request.Status == "PENDING_APPROVAL"),
            approved.Count(request => request.Status != "COMPLETED"),
            requests.Count(request => request.Status == "COMPLETED"),
            requests.Count(request => request.Status == "REJECTED"),
            approved.Sum(request => request.EstimatedTotal),
            requests.Sum(request => request.ActualCost));
    }

    private static DonationSummaryDto Donations(IReadOnlyCollection<DonationView> donations)
    {
        var open = donations.Where(donation => donation.Pledged && donation.Status is "PLEDGED" or "PARTIALLY_RECEIVED").ToList();
        return new DonationSummaryDto(
            open.Count,
            open.Where(donation => donation.DonationType == "MONEY").Sum(donation => Math.Max(0, (donation.PledgedAmount ?? 0) - donation.ReceivedAmount)),
            donations.Sum(donation => donation.ReceivedAmount),
            donations.Sum(donation => donation.ReceivedUsableQuantity),
            donations.Sum(donation => donation.ReceivedUnusableQuantity));
    }

    private static decimal Percent(int part, int total) => total == 0 ? 0 : Math.Round(100m * part / total, 1);

    private static string Upper(string value) =>
        string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant();

    private sealed record Data(
        IReadOnlyList<TaskProjection> Tasks,
        IReadOnlyList<MaterialLine> Lines,
        IReadOnlyList<PurchaseRequestView> Requests,
        IReadOnlyList<DonationView> Donations,
        IReadOnlyList<AttendanceView> Attendance,
        IReadOnlyList<ReadinessAreaView> Readiness);
}
