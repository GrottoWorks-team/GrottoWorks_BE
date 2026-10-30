using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Attendance;
using TaskExecution.Domain.Attendance;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Attendance;

public sealed class AttendanceReadStore(TaskExecutionDbContext db) : IAttendanceReadStore
{
    public async Task<(IReadOnlyList<AttendanceSession> Items,long Total)> PersonalAsync(Guid volunteerId,Guid parishId,AttendanceFilter filter,CancellationToken ct)
    {
        var query=from session in db.AttendanceSessions.AsNoTracking()
                  join assignment in db.Assignments on session.AssignmentId equals assignment.Id
                  join task in db.Tasks on assignment.TaskId equals task.Id
                  join area in db.WorkAreaScopes on task.WorkAreaId equals area.WorkAreaId
                  where assignment.VolunteerId==volunteerId && area.ParishId==parishId &&
                      (!filter.WorkAreaId.HasValue || area.WorkAreaId==filter.WorkAreaId.Value) &&
                      (!filter.SeasonId.HasValue || area.SeasonId==filter.SeasonId.Value)
                  select session;
        if (filter.Status.HasValue) query=query.Where(s=>s.Status==filter.Status.Value);
        if (filter.From.HasValue) query=query.Where(s=>s.CheckInAt>=filter.From.Value);
        if (filter.To.HasValue) query=query.Where(s=>s.CheckInAt<filter.To.Value);
        var total=await query.LongCountAsync(ct);
        var sorted=filter.Descending ? query.OrderByDescending(s=>s.CheckInAt).ThenBy(s=>s.Id) : query.OrderBy(s=>s.CheckInAt).ThenBy(s=>s.Id);
        return (await sorted.Skip((filter.Page-1)*filter.Size).Take(filter.Size).ToListAsync(ct),total);
    }

    public async Task<IReadOnlyList<WorkingHourSummary>> HoursAsync(Guid areaId,DateTimeOffset? rangeStart,DateTimeOffset? rangeEnd,CancellationToken ct)
    {
        var query=from session in db.AttendanceSessions.AsNoTracking()
                  join assignment in db.Assignments on session.AssignmentId equals assignment.Id
                  join task in db.Tasks on assignment.TaskId equals task.Id
                  where task.WorkAreaId==areaId && session.CheckOutAt.HasValue &&
                      (session.Status==AttendanceStatus.Closed || session.Status==AttendanceStatus.Corrected) &&
                      (!rangeStart.HasValue || session.CheckInAt>=rangeStart.Value) && (!rangeEnd.HasValue || session.CheckInAt<rangeEnd.Value)
                  group session by assignment.VolunteerId into sessions
                  select new {VolunteerId=sessions.Key,Minutes=sessions.Sum(s=>(long)s.ApprovedMinutes)};
        var totals=await query.OrderBy(r=>r.VolunteerId).ToListAsync(ct);
        return totals.Select(r=>new WorkingHourSummary(r.VolunteerId,checked((int)r.Minutes),decimal.Round(r.Minutes/60m,2))).ToList();
    }
}
