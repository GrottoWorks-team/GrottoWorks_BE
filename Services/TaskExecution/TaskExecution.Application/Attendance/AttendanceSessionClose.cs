using TaskExecution.Contracts.Events;
using TaskExecution.Domain;

namespace TaskExecution.Application.Attendance;

public sealed partial class AttendanceService
{
    /// <summary>
    /// Trưởng khu đóng phiên hộ khi tình nguyện viên quên check-out.
    /// Trước 11/11/2026 chỉ TNV mới check-out được, nên cảnh báo "phiên điểm danh mở quá lâu"
    /// hiển thị mà không ai xử lý được. Giờ server, `reason` bắt buộc để ghi audit;
    /// số giờ công được tính như check-out thường (từ `checkInAt` tới giờ server).
    /// </summary>
    public async Task<AttendanceDto> CloseSessionAsync(Guid attendanceId,string reason,int? version,
        CancellationToken cancellationToken=default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw DomainRuleException.Validation("CLOSE_REASON_REQUIRED",
                "A reason is required when closing a session on behalf of a volunteer.");
        var session=await store.SessionAsync(attendanceId,cancellationToken)
            ?? throw DomainRuleException.NotFound("ATTENDANCE_NOT_FOUND","The attendance session does not exist.");
        var assignment=await assignments.GetAsync(session.AssignmentId,cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_ASSIGNMENT_NOT_FOUND","The assignment does not exist.");
        var task=await tasks.GetByIdForUpdateAsync(assignment.TaskId,cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND","The task does not exist.");
        var (area,_)=await scopes.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId,cancellationToken);
        await users.EnsureCallerAsync(area.ParishId,cancellationToken);
        var now=DateTimeOffset.UtcNow;
        session.Close(now,version);
        task.RegisterAssignmentChange(now);
        events.Record(TaskEventTypes.SessionClosedByLeader,task,area,new
        {
            attendanceId=session.Id,assignmentId=session.AssignmentId,volunteerUserId=assignment.VolunteerId,
            session.CheckInAt,session.CheckOutAt,session.ApprovedMinutes,reason=reason.Trim(),closedByUserId=user.UserId
        });
        var result=AttendanceDto.From(session).WithContext(assignment.VolunteerId,task.Id,task.Name);
        await store.SaveAsync(cancellationToken);
        return result;
    }
}
