using TaskExecution.Application.Tasks;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;
using TaskExecution.Domain.Assignments;
using TaskState=TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Attendance;

public sealed partial class AttendanceService
{
    public async Task<(IReadOnlyList<AttendanceRequestDto> Items,long Total)> RequestsAsync(Guid areaId,AttendanceRequestStatus status,
        int page,int size,bool descending,CancellationToken cancellationToken=default)
    {
        await scopes.GetWorkAreaForLeaderReadAsync(areaId,cancellationToken);
        var result=await store.RequestsPageAsync(areaId,status,page,size,descending,cancellationToken);
        return (await WithContextAsync(result.Items,cancellationToken),result.Total);
    }

    /// <summary>Đơn vào ca của chính TNV — để app hiển thị "chờ duyệt" thay vì "chưa bắt đầu".</summary>
    public async Task<(IReadOnlyList<AttendanceRequestDto> Items,long Total)> MyRequestsAsync(AttendanceRequestStatus status,
        int page,int size,bool descending,CancellationToken cancellationToken=default)
    {
        var result=await store.MyRequestsPageAsync(user.UserId,status,page,size,descending,cancellationToken);
        return (await WithContextAsync(result.Items,cancellationToken),result.Total);
    }

    /// <summary>
    /// Bổ sung "đơn của ai / cho nhiệm vụ nào" cho danh sách đơn: `volunteerId` đã có sẵn trên
    /// `AttendanceRequest`; `taskId`/`taskName` tra một lượt theo `assignmentId` (tránh N+1).
    /// </summary>
    private async Task<IReadOnlyList<AttendanceRequestDto>> WithContextAsync(IReadOnlyList<AttendanceRequest> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count==0) return Array.Empty<AttendanceRequestDto>();
        var targets=await store.RequestTargetsAsync(rows.Select(r=>r.AssignmentId).Distinct().ToList(),cancellationToken);
        return rows.Select(r=>{
            var target=targets.TryGetValue(r.AssignmentId,out var found) ? found : null;
            return AttendanceRequestDto.WithContext(r,target?.TaskId,target?.TaskName);
        }).ToList();
    }

    public async Task<AttendanceRequestDto> ReviewRequestAsync(Guid requestId,bool approve,string? note,int? version,CancellationToken cancellationToken=default)
    {
        var request=await store.RequestAsync(requestId,cancellationToken)
            ?? throw DomainRuleException.NotFound("ATTENDANCE_REQUEST_NOT_FOUND","The request does not exist.");
        return await store.ForVolunteerAsync(request.VolunteerId,async () =>
        {
            var assignment=await assignments.GetAsync(request.AssignmentId,cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_ASSIGNMENT_NOT_FOUND","The assignment does not exist.");
            var task=await tasks.GetByIdForUpdateAsync(assignment.TaskId,cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND","The task does not exist.");
            var (area,_)=await scopes.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId,cancellationToken);
            await users.EnsureCallerAsync(area.ParishId,cancellationToken);
            request.EnsurePending(version);
            Guid? sessionId=null;
            if (approve)
            {
                EnsureTaskAllowsCheckIn(task);
                if (assignment.Status!=AssignmentStatus.Accepted)
                    throw new DomainRuleException("ASSIGNMENT_NOT_ACCEPTED","The assignment is no longer accepted.");
                // 12/11/2026: duyệt đơn là lúc việc THẬT SỰ bắt đầu → áp cùng điều kiện bắt đầu.
                await startEligibility.EnsureCanStartAsync(task,cancellationToken);
                if (await store.OverlapsAsync(request.VolunteerId,request.RequestedAt,null,null,cancellationToken))
                    throw new DomainRuleException("ATTENDANCE_OVERLAP","The session would overlap another session.");
                sessionId=Guid.NewGuid();
            }
            var previous=task.Status;
            request.Review(approve,user.UserId,note,sessionId,version,DateTimeOffset.UtcNow);
            if (approve)
            {
                store.Add(AttendanceSession.Open(request));
                task.BeginWork(DateTimeOffset.UtcNow);
            }
            else task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            events.Record(TaskEventTypes.AttendanceReviewed,task,area,new
            {
                requestId,taskId=task.Id,workAreaId=area.WorkAreaId,reviewType="CHECK_IN",
                assignmentId=assignment.Id,attendanceId=sessionId,volunteerUserId=request.VolunteerId,
                decision=approve ? "APPROVED" : "REJECTED",reviewNote=request.ReviewNote
            });
            if (approve && previous==TaskState.NotStarted)
                events.Record(TaskEventTypes.StatusChanged,task,area,new {taskId=task.Id,workAreaId=area.WorkAreaId,previousStatus="NOT_STARTED",status="IN_PROGRESS"});
            await store.SaveAsync(cancellationToken);
            return AttendanceRequestDto.From(request);
        },cancellationToken);
    }
}
