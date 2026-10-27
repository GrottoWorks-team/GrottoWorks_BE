using Microsoft.EntityFrameworkCore;
using Reporting.Application.ReadModels;
using Reporting.Domain.ReadModels;
using Reporting.Domain.Scope;
using Reporting.Infrastructure.Data;

namespace Reporting.Infrastructure.ReadModels;

/// <summary>
/// Gets use <c>FindAsync</c> so a row added earlier in the same event (two lines of one material) is found before it is
/// saved. Saved together with the inbox row by <see cref="Recognition.ContributionStore.SaveChangesAsync"/>.
/// </summary>
public sealed class ReadModelStore(ReportingDbContext dbContext) : IReadModelStore
{
    public async Task<MaterialLine?> GetMaterialLineAsync(Guid workAreaId, Guid materialId, CancellationToken cancellationToken = default) =>
        await dbContext.MaterialLines.FindAsync([workAreaId, materialId], cancellationToken);

    public async Task<PurchaseRequestView?> GetPurchaseRequestAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default) =>
        await dbContext.PurchaseRequestViews.FindAsync([purchaseRequestId], cancellationToken);

    public async Task<DonationView?> GetDonationAsync(Guid donationId, CancellationToken cancellationToken = default) =>
        await dbContext.DonationViews.FindAsync([donationId], cancellationToken);

    public async Task<AttendanceView?> GetAttendanceAsync(Guid attendanceId, CancellationToken cancellationToken = default) =>
        await dbContext.AttendanceViews.FindAsync([attendanceId], cancellationToken);

    public async Task<ReadinessAreaView?> GetReadinessAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
        await dbContext.ReadinessAreaViews.FindAsync([workAreaId], cancellationToken);

    public void Add(MaterialLine line) => dbContext.MaterialLines.Add(line);

    public void Add(PurchaseRequestView request) => dbContext.PurchaseRequestViews.Add(request);

    public void Add(DonationView donation) => dbContext.DonationViews.Add(donation);

    public void Add(AttendanceView attendance) => dbContext.AttendanceViews.Add(attendance);

    public void Add(ReadinessAreaView area) => dbContext.ReadinessAreaViews.Add(area);

    public void Add(ReadinessHistoryEntry entry) => dbContext.ReadinessHistory.Add(entry);

    public async Task<IReadOnlyList<MaterialLine>> ListMaterialLinesAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        await dbContext.MaterialLines.AsNoTracking().Where(line => workAreaIds.Contains(line.WorkAreaId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PurchaseRequestView>> ListPurchaseRequestsAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        await dbContext.PurchaseRequestViews.AsNoTracking().Where(request => workAreaIds.Contains(request.WorkAreaId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DonationView>> ListDonationsAsync(Guid parishId, Guid seasonId, CancellationToken cancellationToken = default) =>
        await dbContext.DonationViews.AsNoTracking()
            .Where(donation => donation.ParishId == parishId && donation.SeasonId == seasonId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AttendanceView>> ListAttendanceAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        await dbContext.AttendanceViews.AsNoTracking().Where(attendance => workAreaIds.Contains(attendance.WorkAreaId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReadinessAreaView>> ListReadinessAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        await dbContext.ReadinessAreaViews.AsNoTracking().Where(area => workAreaIds.Contains(area.WorkAreaId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReadinessHistoryEntry>> ListReadinessHistoryAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        await dbContext.ReadinessHistory.AsNoTracking().Where(entry => workAreaIds.Contains(entry.WorkAreaId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskProjection>> ListTasksAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        await dbContext.TaskProjections.AsNoTracking().Where(task => workAreaIds.Contains(task.WorkAreaId)).ToListAsync(cancellationToken);
}
