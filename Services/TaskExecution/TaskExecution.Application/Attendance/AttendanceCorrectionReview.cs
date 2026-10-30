using TaskExecution.Contracts.Events;
using TaskExecution.Domain;

namespace TaskExecution.Application.Attendance;

public sealed partial class AttendanceService
{
    public async Task<AttendanceCorrectionDto> ReviewCorrectionAsync(Guid correctionId,bool approve,string? note,int? version,CancellationToken cancellationToken=default)
    {
        var correction=await store.CorrectionAsync(correctionId,cancellationToken)
            ?? throw DomainRuleException.NotFound("ATTENDANCE_CORRECTION_NOT_FOUND","The correction does not exist.");
        return await store.ForVolunteerAsync(correction.RequestedBy,async () =>
        {
            var session=await store.SessionAsync(correction.AttendanceId,cancellationToken)
                ?? throw DomainRuleException.NotFound("ATTENDANCE_NOT_FOUND","The attendance does not exist.");
            var assignment=await assignments.GetAsync(session.AssignmentId,cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_ASSIGNMENT_NOT_FOUND","The assignment does not exist.");
            var task=await tasks.GetByIdForUpdateAsync(assignment.TaskId,cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND","The task does not exist.");
            var (area,_)=await scopes.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId,cancellationToken);
            await users.EnsureCallerAsync(area.ParishId,cancellationToken);
            correction.EnsureReviewAllowed(approve,note,version);
            var previousMinutes=session.ApprovedMinutes;
            if (approve)
            {
                session.EnsureVersion(correction.SessionVersion);
                if (await store.OverlapsAsync(assignment.VolunteerId,correction.RequestedCheckInAt,correction.RequestedCheckOutAt,session.Id,cancellationToken))
                    throw new DomainRuleException("ATTENDANCE_OVERLAP","Corrected times overlap another session.");
                session.Correct(correction.RequestedCheckInAt,correction.RequestedCheckOutAt,correction.SessionVersion);
            }
            correction.Review(approve,user.UserId,note,version,DateTimeOffset.UtcNow);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            if (approve)
                events.Record(TaskEventTypes.HoursApproved,task,area,new
                {
                    attendanceId=session.Id,assignmentId=assignment.Id,volunteerUserId=assignment.VolunteerId,
                    session.CheckInAt,session.CheckOutAt,session.ApprovedMinutes,previousApprovedMinutes=previousMinutes,correctionId
                });
            await store.SaveAsync(cancellationToken);
            return AttendanceCorrectionDto.From(correction);
        },cancellationToken);
    }
}
