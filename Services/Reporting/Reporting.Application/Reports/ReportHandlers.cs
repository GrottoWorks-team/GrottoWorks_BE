using BuildingBlocks.Security;
using Reporting.Application.ReadModels;
using Reporting.Domain.ReadModels;
using Reporting.Domain.Scope;

namespace Reporting.Application.Reports;

public sealed record ReportColumnDto(string Key, string Label, string? Unit = null);

public sealed record ReportFiltersDto(Guid SeasonId, Guid? WorkAreaId, Guid? CommunityId, DateOnly? From, DateOnly? To);

/// <summary>
/// <c>ReportData</c> (F-RPT-05, BA 12.2): parish, season, generation time, filters, units and data scope are always stated
/// (BR-57). Rows are flat objects keyed by <see cref="Columns"/> so the same data feeds the export (F-RPT-06).
/// </summary>
public sealed record ReportDataDto(
    ReportType ReportType,
    Guid ParishId,
    Guid SeasonId,
    DateTimeOffset GeneratedAt,
    Guid GeneratedBy,
    string Scope,
    ReportFiltersDto Filters,
    IReadOnlyList<ReportColumnDto> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyDictionary<string, object?> Totals,
    IReadOnlyList<string> Notes);

/// <summary>F-RPT-05: the six operational reports, built from the read models of the caller's scope.</summary>
public sealed class ReportQueryHandler(ReportScopeResolver resolver, IReadModelStore store, ICurrentUser currentUser)
{
    private const string Money = "VND";

    public async Task<ReportDataDto> BuildAsync(ReportType type, ReportQuery query, CancellationToken cancellationToken = default)
    {
        resolver.EnsureRole(type);
        var scope = await resolver.ResolveAsync(query, cancellationToken);
        var names = scope.WorkAreas.ToDictionary(area => area.WorkAreaId, area => area.Name);
        var (columns, rows, totals, notes) = type switch
        {
            ReportType.Tasks => await TasksAsync(scope, query, names, cancellationToken),
            ReportType.Materials => await MaterialsAsync(scope, names, cancellationToken),
            ReportType.Purchases => await PurchasesAsync(scope, query, names, cancellationToken),
            ReportType.Contributions => await ContributionsAsync(scope, query, names, cancellationToken),
            ReportType.Attendance => await AttendanceAsync(scope, query, names, cancellationToken),
            _ => await ReadinessAsync(scope, query, names, cancellationToken)
        };

        return new ReportDataDto(type, scope.ParishId, scope.SeasonId, DateTimeOffset.UtcNow, currentUser.UserId, scope.Scope,
            new ReportFiltersDto(query.SeasonId, query.WorkAreaId, query.CommunityId, query.From, query.To), columns, rows, totals, notes);
    }

    private async Task<Parts> TasksAsync(ResolvedScope scope, ReportQuery query, Dictionary<Guid, string> names, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = (await store.ListTasksAsync(scope.Ids, cancellationToken))
            .Where(task => query.InRange(task.DueAt))
            .OrderBy(task => names[task.WorkAreaId], StringComparer.CurrentCulture)
            .ThenBy(task => task.DueAt ?? DateTimeOffset.MaxValue)
            .ToList();
        var rows = tasks.Select(task => Row(
                ("workArea", names[task.WorkAreaId]),
                ("task", task.Name),
                ("type", Upper(task.Type.ToString())),
                ("status", Upper(task.Status.ToString())),
                ("assignees", task.AssigneeUserIds.Length),
                ("dueAt", task.DueAt),
                ("overdue", task.IsOverdue(now))))
            .ToList();
        var totals = new Dictionary<string, object?>
        {
            ["tasks"] = tasks.Count,
            ["completed"] = tasks.Count(task => task.Status == ProjectedTaskStatus.Completed),
            ["overdue"] = tasks.Count(task => task.IsOverdue(now)),
            ["unassigned"] = tasks.Count(task => !task.IsClosed && task.AssigneeUserIds.Length == 0),
            ["revisionRequired"] = tasks.Count(task => task.Status == ProjectedTaskStatus.RevisionRequired)
        };
        return new Parts(
            [new("workArea", "Khu vực"), new("task", "Task"), new("type", "Loại"), new("status", "Trạng thái"), new("assignees", "Số người nhận"),
                new("dueAt", "Hạn"), new("overdue", "Trễ hạn")],
            rows, totals, ["Bộ lọc thời gian áp dụng theo hạn task."]);
    }

    private async Task<Parts> MaterialsAsync(ResolvedScope scope, Dictionary<Guid, string> names, CancellationToken cancellationToken)
    {
        var lines = (await store.ListMaterialLinesAsync(scope.Ids, cancellationToken))
            .OrderBy(line => names[line.WorkAreaId], StringComparer.CurrentCulture)
            .ThenBy(line => line.MaterialName ?? line.MaterialId.ToString(), StringComparer.CurrentCulture)
            .ToList();
        var rows = lines.Select(line => Row(
                ("workArea", names[line.WorkAreaId]),
                ("materialCode", line.MaterialCode),
                ("material", line.MaterialName ?? line.MaterialId.ToString()),
                ("unit", line.Unit),
                ("required", line.Required),
                ("confirmedAvailable", line.ConfirmedAvailable),
                ("shortage", line.Shortage),
                ("pledged", line.Pledged),
                ("donatedUsable", line.DonatedUsable),
                ("donatedUnusable", line.DonatedUnusable),
                ("purchasedUsable", line.PurchasedUsable),
                ("purchasedUnusable", line.PurchasedUnusable)))
            .ToList();
        var totals = new Dictionary<string, object?>
        {
            ["lines"] = lines.Count,
            ["linesWithShortage"] = lines.Count(line => line.Shortage > 0)
        };
        return new Parts(
            [new("workArea", "Khu vực"), new("materialCode", "Mã"), new("material", "Vật tư"), new("unit", "Đơn vị"), new("required", "Cần", "unit"),
                new("confirmedAvailable", "Đã có", "unit"), new("shortage", "Còn thiếu", "unit"), new("pledged", "Cam kết tặng", "unit"),
                new("donatedUsable", "Được tặng (dùng được)", "unit"), new("donatedUnusable", "Được tặng (không dùng được)", "unit"),
                new("purchasedUsable", "Đã mua (dùng được)", "unit"), new("purchasedUnusable", "Đã mua (không dùng được)", "unit")],
            rows, totals,
            ["Số lượng tính theo đơn vị của từng vật tư; cần / đã có / còn thiếu là số liệu tại lần thay đổi shortage gần nhất.",
                "Shortage không bao giờ âm (BR-25)."]);
    }

    private async Task<Parts> PurchasesAsync(ResolvedScope scope, ReportQuery query, Dictionary<Guid, string> names, CancellationToken cancellationToken)
    {
        var requests = (await store.ListPurchaseRequestsAsync(scope.Ids, cancellationToken))
            .Where(request => query.InRange(request.SubmittedAt ?? request.UpdatedAt))
            .OrderBy(request => names[request.WorkAreaId], StringComparer.CurrentCulture)
            .ThenBy(request => request.SubmittedAt)
            .ToList();
        var rows = requests.Select(request => Row(
                ("workArea", names[request.WorkAreaId]),
                ("purchaseRequestId", request.PurchaseRequestId),
                ("status", request.Status),
                ("estimatedTotal", request.EstimatedTotal),
                ("actualCost", request.ActualCost),
                ("deliveries", request.Deliveries),
                ("submittedAt", request.SubmittedAt),
                ("decidedAt", request.DecidedAt)))
            .ToList();
        var approved = requests.Where(request => request.Status is not ("PENDING_APPROVAL" or "REJECTED" or "")).ToList();
        var totals = new Dictionary<string, object?>
        {
            ["requests"] = requests.Count,
            ["pendingApproval"] = requests.Count(request => request.Status == "PENDING_APPROVAL"),
            ["rejected"] = requests.Count(request => request.Status == "REJECTED"),
            ["estimatedApproved"] = approved.Sum(request => request.EstimatedTotal),
            ["actualCost"] = requests.Sum(request => request.ActualCost)
        };
        return new Parts(
            [new("workArea", "Khu vực"), new("purchaseRequestId", "Purchase request"), new("status", "Trạng thái"),
                new("estimatedTotal", "Dự toán", Money), new("actualCost", "Chi phí thực tế", Money), new("deliveries", "Số lần giao"),
                new("submittedAt", "Gửi duyệt"), new("decidedAt", "Quyết định")],
            rows, totals, [$"Tiền tính bằng {Money}."]);
    }

    private async Task<Parts> ContributionsAsync(ResolvedScope scope, ReportQuery query, Dictionary<Guid, string> names, CancellationToken cancellationToken)
    {
        var areas = scope.Ids.ToHashSet();
        var donations = (await store.ListDonationsAsync(scope.ParishId, scope.SeasonId, cancellationToken))
            .Where(donation => donation.WorkAreaId is { } area ? areas.Contains(area) : scope.WholeParish && query.WorkAreaId is null && query.CommunityId is null)
            .Where(donation => query.InRange(donation.FirstReceivedAt ?? donation.UpdatedAt))
            .OrderBy(donation => donation.FirstReceivedAt ?? donation.UpdatedAt)
            .ToList();
        var rows = donations.Select(donation => Row(
                ("donorId", donation.DonorId),
                ("hasAccount", donation.DonorUserId is not null),
                ("donationType", donation.DonationType),
                ("workArea", donation.WorkAreaId is { } area ? names[area] : null),
                ("status", donation.Status),
                ("pledgedAmount", donation.PledgedAmount),
                ("receivedAmount", donation.ReceivedAmount),
                ("pledgedQuantity", donation.Pledged && donation.DonationType == "MATERIAL" ? donation.PledgedQuantity : null),
                ("receivedUsableQuantity", donation.ReceivedUsableQuantity),
                ("receivedUnusableQuantity", donation.ReceivedUnusableQuantity),
                ("receipts", donation.Receipts),
                ("firstReceivedAt", donation.FirstReceivedAt)))
            .ToList();
        var totals = new Dictionary<string, object?>
        {
            ["donations"] = donations.Count,
            ["openPledges"] = donations.Count(donation => donation.Status is "PLEDGED" or "PARTIALLY_RECEIVED" && donation.Pledged),
            ["receivedMoney"] = donations.Sum(donation => donation.ReceivedAmount),
            ["receivedUsableQuantity"] = donations.Sum(donation => donation.ReceivedUsableQuantity),
            ["receivedUnusableQuantity"] = donations.Sum(donation => donation.ReceivedUnusableQuantity)
        };
        return new Parts(
            [new("donorId", "Donor"), new("hasAccount", "Có tài khoản"), new("donationType", "Loại"), new("workArea", "Khu vực nhận"),
                new("status", "Trạng thái"), new("pledgedAmount", "Tiền cam kết", Money), new("receivedAmount", "Tiền đã nhận", Money),
                new("pledgedQuantity", "Vật tư cam kết", "unit"), new("receivedUsableQuantity", "Vật tư nhận (dùng được)", "unit"),
                new("receivedUnusableQuantity", "Vật tư nhận (không dùng được)", "unit"), new("receipts", "Số lần nhận"),
                new("firstReceivedAt", "Nhận lần đầu")],
            rows, totals,
            [$"Tiền chỉ tính từ donation đã thực nhận ({Money}); pledge không phải tiền đã nhận (BR-39).",
                "Số lượng vật tư là tổng các dòng, mỗi dòng theo đơn vị của vật tư.",
                "Đồ mượn chưa có trong báo cáo (chưa có event trả đồ)."]);
    }

    private async Task<Parts> AttendanceAsync(ResolvedScope scope, ReportQuery query, Dictionary<Guid, string> names, CancellationToken cancellationToken)
    {
        var attendance = (await store.ListAttendanceAsync(scope.Ids, cancellationToken))
            .Where(item => query.InRange(item.ApprovedAt))
            .ToList();
        var rows = attendance
            .GroupBy(item => (item.VolunteerUserId, item.WorkAreaId))
            .Select(group => Row(
                ("volunteerUserId", group.Key.VolunteerUserId),
                ("workArea", names[group.Key.WorkAreaId]),
                ("sessions", group.Count()),
                ("approvedHours", group.Sum(item => item.ApprovedHours))))
            .OrderBy(row => (string)row["workArea"]!, StringComparer.CurrentCulture)
            .ThenByDescending(row => (decimal)row["approvedHours"]!)
            .ToList();
        var totals = new Dictionary<string, object?>
        {
            ["volunteers"] = attendance.Select(item => item.VolunteerUserId).Distinct().Count(),
            ["sessions"] = attendance.Count,
            ["approvedHours"] = attendance.Sum(item => item.ApprovedHours)
        };
        return new Parts(
            [new("volunteerUserId", "Volunteer"), new("workArea", "Khu vực"), new("sessions", "Số lần"), new("approvedHours", "Giờ được duyệt", "h")],
            rows, totals, ["Chỉ giờ đã được duyệt (BR-21); bộ lọc thời gian theo ngày duyệt."]);
    }

    private async Task<Parts> ReadinessAsync(ResolvedScope scope, ReportQuery query, Dictionary<Guid, string> names, CancellationToken cancellationToken)
    {
        var states = (await store.ListReadinessAsync(scope.Ids, cancellationToken)).ToDictionary(state => state.WorkAreaId);
        var history = (await store.ListReadinessHistoryAsync(scope.Ids, cancellationToken))
            .Where(entry => query.InRange(entry.OccurredAt))
            .ToList();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var area in scope.WorkAreas)
        {
            var state = states.GetValueOrDefault(area.WorkAreaId);
            rows.Add(Row(
                ("workArea", area.Name), ("kind", "SUMMARY"), ("status", state?.Status ?? "NO_RESULT"), ("item", null), ("mandatory", null),
                ("note", $"{state?.FailedMandatoryItems ?? 0} mandatory failed, reopened {state?.ReopenCount ?? 0}x"), ("occurredAt", state?.UpdatedAt)));
            rows.AddRange(history.Where(entry => entry.WorkAreaId == area.WorkAreaId).OrderBy(entry => entry.OccurredAt).Select(entry => Row(
                ("workArea", area.Name), ("kind", entry.Kind), ("status", null), ("item", entry.ItemName), ("mandatory", entry.Mandatory),
                ("note", entry.Note), ("occurredAt", entry.OccurredAt))));
        }

        var totals = new Dictionary<string, object?>
        {
            ["workAreas"] = scope.WorkAreas.Count,
            ["ready"] = states.Values.Count(state => state.Status == "READY"),
            ["needsCorrection"] = states.Values.Count(state => state.Status == "NEEDS_CORRECTION"),
            ["failedMandatoryItems"] = states.Values.Sum(state => state.FailedMandatoryItems)
        };
        return new Parts(
            [new("workArea", "Khu vực"), new("kind", "Loại"), new("status", "Trạng thái"), new("item", "Hạng mục"), new("mandatory", "Bắt buộc"),
                new("note", "Ghi chú / lý do"), new("occurredAt", "Thời điểm")],
            rows, totals, ["Số item failed tính từ lần READY gần nhất (Readiness chưa phát event khi recheck đạt)."]);
    }

    private static IReadOnlyDictionary<string, object?> Row(params (string Key, object? Value)[] cells) =>
        cells.ToDictionary(cell => cell.Key, cell => cell.Value);

    /// <summary>PascalCase enum name → SNAKE_CASE_UPPER as in the API.</summary>
    private static string Upper(string value) =>
        string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant();

    private sealed record Parts(
        IReadOnlyList<ReportColumnDto> Columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
        IReadOnlyDictionary<string, object?> Totals,
        IReadOnlyList<string> Notes);
}
