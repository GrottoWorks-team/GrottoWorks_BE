using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Attendance;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Attendance;

public sealed partial class AttendanceStore(TaskExecutionDbContext db) : IAttendanceStore
{
    public async Task<T> ForVolunteerAsync<T>(Guid volunteerId,Func<Task<T>> action,CancellationToken cancellationToken)
    {
        await using var transaction=await db.Database.BeginTransactionAsync(cancellationToken);
        var key=$"attendance-volunteer:{volunteerId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))",cancellationToken);
        try
        {
            var result=await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch(DbUpdateConcurrencyException)
        {
            throw new DomainRuleException("ATTENDANCE_VERSION_MISMATCH","Attendance or task changed concurrently.",DomainErrorKind.PreconditionFailed);
        }
    }

    public Task<AttendanceRequest?> RequestAsync(Guid id,CancellationToken ct) => db.AttendanceRequests.SingleOrDefaultAsync(r=>r.Id==id,ct);
    public Task<AttendanceSession?> SessionAsync(Guid id,CancellationToken ct) => db.AttendanceSessions.SingleOrDefaultAsync(r=>r.Id==id,ct);
    public Task<AttendanceSession?> OpenSessionAsync(Guid id,CancellationToken ct) => db.AttendanceSessions.SingleOrDefaultAsync(r=>r.AssignmentId==id && r.CheckOutAt==null,ct);
    public async Task<bool> HasPendingOrOpenAsync(Guid volunteerId,CancellationToken ct) =>
        await db.AttendanceRequests.AnyAsync(r=>r.VolunteerId==volunteerId && r.Status==AttendanceRequestStatus.Pending,ct) ||
        await (from s in db.AttendanceSessions join a in db.Assignments on s.AssignmentId equals a.Id
               where a.VolunteerId==volunteerId && s.CheckOutAt==null select s.Id).AnyAsync(ct);

    public Task<bool> OverlapsAsync(Guid volunteerId,DateTimeOffset start,DateTimeOffset? end,Guid? excludeSession,CancellationToken ct) =>
        (from s in db.AttendanceSessions join a in db.Assignments on s.AssignmentId equals a.Id
         where a.VolunteerId==volunteerId && (!excludeSession.HasValue || s.Id!=excludeSession.Value) &&
             (!end.HasValue || s.CheckInAt<end.Value) && (!s.CheckOutAt.HasValue || s.CheckOutAt>start)
         select s.Id).AnyAsync(ct);

    public Task<AttendanceReplay?> ReplayAsync(Guid actorId,Guid key,CancellationToken ct) => db.AttendanceReplays.FindAsync([actorId,key],ct).AsTask();
    public void Add(AttendanceRequest row) => db.AttendanceRequests.Add(row);
    public void Add(AttendanceSession row) => db.AttendanceSessions.Add(row);
    public void Add(AttendanceReplay row) => db.AttendanceReplays.Add(row);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
