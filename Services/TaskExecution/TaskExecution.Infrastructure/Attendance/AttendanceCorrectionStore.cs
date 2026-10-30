using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.Infrastructure.Attendance;

public sealed partial class AttendanceStore
{
    public Task<AttendanceCorrection?> CorrectionAsync(Guid id,CancellationToken ct)=>db.AttendanceCorrections.SingleOrDefaultAsync(r=>r.Id==id,ct);
    public Task<bool> HasPendingCorrectionAsync(Guid id,CancellationToken ct)=>db.AttendanceCorrections.AnyAsync(r=>r.AttendanceId==id && r.Status==AttendanceCorrectionStatus.Pending,ct);
    public void Add(AttendanceCorrection row)=>db.AttendanceCorrections.Add(row);

    public async Task<(IReadOnlyList<AttendanceCorrection> Items,long Total)> CorrectionsPageAsync(Guid areaId,AttendanceCorrectionStatus status,int page,int size,bool descending,CancellationToken ct)
    {
        var query=from correction in db.AttendanceCorrections.AsNoTracking()
                  join session in db.AttendanceSessions on correction.AttendanceId equals session.Id
                  join assignment in db.Assignments on session.AssignmentId equals assignment.Id
                  join task in db.Tasks on assignment.TaskId equals task.Id
                  where task.WorkAreaId==areaId && correction.Status==status select correction;
        var total=await query.LongCountAsync(ct);
        var sorted=descending ? query.OrderByDescending(r=>r.CreatedAt).ThenBy(r=>r.Id) : query.OrderBy(r=>r.CreatedAt).ThenBy(r=>r.Id);
        return (await sorted.Skip((page-1)*size).Take(size).ToListAsync(ct),total);
    }
}
