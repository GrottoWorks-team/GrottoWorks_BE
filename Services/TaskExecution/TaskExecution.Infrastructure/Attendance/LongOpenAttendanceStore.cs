using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Application.Attendance;
using TaskExecution.Domain.Attendance;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Attendance;

public sealed class LongOpenAttendanceStore(TaskExecutionDbContext db) : ILongOpenAttendanceStore
{
    public async Task<IReadOnlyList<AttendanceSession>> ListAsync(DateTimeOffset cutoff,Guid? parishId,Guid? leaderId,CancellationToken ct)=>
        await (from session in db.AttendanceSessions.AsNoTracking()
               join assignment in db.Assignments on session.AssignmentId equals assignment.Id
               join task in db.Tasks on assignment.TaskId equals task.Id
               join area in db.WorkAreaScopes on task.WorkAreaId equals area.WorkAreaId
               where !session.CheckOutAt.HasValue && session.CheckInAt<=cutoff &&
                   (!parishId.HasValue || area.ParishId==parishId.Value) && (!leaderId.HasValue || area.LeaderUserId==leaderId.Value)
               orderby session.CheckInAt,session.Id select session).ToListAsync(ct);

    public async Task<IReadOnlyList<AttendanceAlertCandidate>> CandidatesAsync(DateTimeOffset cutoff,Guid? after,int size,CancellationToken ct)=>
        await (from session in db.AttendanceSessions.AsNoTracking()
               join assignment in db.Assignments on session.AssignmentId equals assignment.Id
               where !session.CheckOutAt.HasValue && session.CheckInAt<=cutoff && (!after.HasValue || session.Id.CompareTo(after.Value)>0)
               orderby session.Id select new AttendanceAlertCandidate(session.Id,assignment.VolunteerId)).Take(size).ToListAsync(ct);
    public Task<bool> HasAlertAsync(Guid id,int threshold,CancellationToken ct)=>db.AttendanceAlerts.AnyAsync(r=>r.AttendanceId==id && r.ThresholdMinutes==threshold,ct);
    public void Add(AttendanceSessionAlert alert)=>db.AttendanceAlerts.Add(alert);
}

public sealed class AttendanceSessionAlertConfiguration : IEntityTypeConfiguration<AttendanceSessionAlert>
{
    public void Configure(EntityTypeBuilder<AttendanceSessionAlert> b)
    {
        b.ToTable("attendance_session_alert");b.HasKey(r=>new {r.AttendanceId,r.ThresholdMinutes});
        b.Property(r=>r.AttendanceId).HasColumnName("attendance_id");
        b.Property(r=>r.ThresholdMinutes).HasColumnName("threshold_minutes");b.Property(r=>r.CreatedAt).HasColumnName("created_at");
        b.HasOne<AttendanceSession>().WithMany().HasForeignKey(r=>r.AttendanceId).OnDelete(DeleteBehavior.Restrict);
    }
}
