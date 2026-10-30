using System.Text.Json;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;

namespace TaskExecution.Application.Attendance;

public sealed partial class AttendanceService
{
    public async Task<AttendanceDto> CheckOutAsync(Guid assignmentId,int? version,Guid key,CancellationToken cancellationToken=default)
    {
        if (key==Guid.Empty) throw DomainRuleException.Validation("IDEMPOTENCY_KEY_REQUIRED","A UUID Idempotency-Key is required.");
        return await store.ForVolunteerAsync(user.UserId,async () =>
        {
            await EnsureCallerActiveAsync(cancellationToken);
            var fingerprint=Fingerprint(new {operation="CHECK_OUT",assignmentId,version});
            var cached=await store.ReplayAsync(user.UserId,key,cancellationToken);
            if (cached is not null) return Replay<AttendanceDto>(cached,fingerprint);
            var (_,task,area)=await VolunteerAssignmentAsync(assignmentId,cancellationToken);
            var session=await store.OpenSessionAsync(assignmentId,cancellationToken)
                ?? throw new DomainRuleException("ATTENDANCE_NOT_OPEN","No open session exists for this assignment.");
            session.Close(DateTimeOffset.UtcNow,version);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            events.Record(TaskEventTypes.HoursApproved,task,area,new
            {
                attendanceId=session.Id,assignmentId,volunteerUserId=user.UserId,
                session.CheckInAt,session.CheckOutAt,session.ApprovedMinutes,previousApprovedMinutes=0
            });
            var result=AttendanceDto.From(session);
            store.Add(AttendanceReplay.Create(user.UserId,key,fingerprint,JsonSerializer.Serialize(result)));
            await store.SaveAsync(cancellationToken);
            return result;
        },cancellationToken);
    }
}
