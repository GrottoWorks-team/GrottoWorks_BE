using BuildingBlocks.Security;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.Application.Attendance;

public sealed record LongOpenAttendanceOptions(int ThresholdMinutes=720);
public sealed record AttendanceAlertCandidate(Guid SessionId,Guid VolunteerId);

public sealed class AttendanceSessionAlert
{
    private AttendanceSessionAlert() { }
    public Guid AttendanceId {get;private set;}
    public int ThresholdMinutes {get;private set;}
    public DateTimeOffset CreatedAt {get;private set;}
    public static AttendanceSessionAlert Create(Guid id,int threshold,DateTimeOffset now)=>new() {AttendanceId=id,ThresholdMinutes=threshold,CreatedAt=now};
}

public interface ILongOpenAttendanceStore
{
    Task<IReadOnlyList<AttendanceSession>> ListAsync(DateTimeOffset cutoff,Guid? parishId,Guid? leaderId,Guid? workAreaId,CancellationToken ct);
    Task<IReadOnlyList<AttendanceAlertCandidate>> CandidatesAsync(DateTimeOffset cutoff,Guid? after,int size,CancellationToken ct);
    Task<bool> HasAlertAsync(Guid sessionId,int thresholdMinutes,CancellationToken ct);
    void Add(AttendanceSessionAlert alert);
}

/// <summary>
/// Cảnh báo "phiên điểm danh mở quá lâu" cho trưởng khu. Ngoài thời điểm check-in, DTO còn kèm
/// `volunteerId`/`taskId`/`taskName` để UI nói rõ phiên của ai, nhiệm vụ nào (trước đây chỉ có giờ).
/// </summary>
public sealed class LongOpenAttendanceQuery(ILongOpenAttendanceStore store,LongOpenAttendanceOptions options,ICurrentUser user,
    IAssignmentStore? assignments=null,ITaskRepository? tasks=null)
{
    public async Task<IReadOnlyList<AttendanceDto>> HandleAsync(int? hours,Guid? workAreaId=null,CancellationToken ct=default)
    {
        if (hours is <=0 or >9999)
            throw new DomainRuleException("INVALID_DURATION","Hours must be between 1 and 9999.",DomainErrorKind.BadRequest);
        if (user.Role is not (GrottoWorksRoles.Leader or GrottoWorksRoles.Admin))
            throw DomainRuleException.Forbidden("ATTENDANCE_ACCESS_DENIED","Leader or admin access is required.");
        if (user.Role==GrottoWorksRoles.Leader && !user.ParishId.HasValue)
            throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED","A leader parish is required.");
        var cutoff=DateTimeOffset.UtcNow.AddMinutes(-(hours*60 ?? options.ThresholdMinutes));
        var rows=await store.ListAsync(cutoff,user.Role==GrottoWorksRoles.Admin ? null : user.ParishId,
            user.Role==GrottoWorksRoles.Admin ? null : user.UserId,workAreaId,ct);
        var result=new List<AttendanceDto>(rows.Count);
        foreach (var row in rows)
        {
            Guid? taskId=null; string? taskName=null; Guid? volunteerId=null;
            if (assignments is not null)
            {
                var assignment=await assignments.GetAsync(row.AssignmentId,ct);
                volunteerId=assignment?.VolunteerId;
                if (assignment is not null && tasks is not null)
                {
                    var task=await tasks.GetByIdAsync(assignment.TaskId,ct);
                    taskId=task?.Id; taskName=task?.Name;
                }
            }
            result.Add(AttendanceDto.From(row).WithContext(volunteerId,taskId,taskName));
        }
        return result;
    }
}

public sealed class AttendanceAlertProcessor(ILongOpenAttendanceStore alerts,IAttendanceStore attendance,IAssignmentStore assignments,
    ITaskRepository tasks,IScopeProjectionStore scopes,TaskEventRecorder events,LongOpenAttendanceOptions options)
{
    public async Task ScanAsync(DateTimeOffset now,CancellationToken ct=default)
    {
        var cutoff=now.AddMinutes(-options.ThresholdMinutes);
        Guid? after=null;
        while (!ct.IsCancellationRequested)
        {
            var batch=await alerts.CandidatesAsync(cutoff,after,100,ct);
            if (batch.Count==0) return;
            foreach (var candidate in batch)
            {
                await attendance.ForVolunteerAsync(candidate.VolunteerId,async () =>
                {
                    var session=await attendance.SessionAsync(candidate.SessionId,ct);
                    if (session is null || session.CheckOutAt.HasValue || session.CheckInAt>cutoff ||
                        await alerts.HasAlertAsync(session.Id,options.ThresholdMinutes,ct)) return false;
                    var assignment=await assignments.GetAsync(session.AssignmentId,ct)
                        ?? throw DomainRuleException.NotFound("TASK_ASSIGNMENT_NOT_FOUND","The session assignment does not exist.");
                    var task=await tasks.GetByIdForUpdateAsync(assignment.TaskId,ct)
                        ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND","The session task does not exist.");
                    var area=await scopes.GetWorkAreaAsync(task.WorkAreaId,ct)
                        ?? throw DomainRuleException.NotFound("WORK_AREA_NOT_FOUND","The session work area is missing.");
                    alerts.Add(AttendanceSessionAlert.Create(session.Id,options.ThresholdMinutes,now));
                    events.Record(TaskEventTypes.SessionOpenTooLong,task,area,new
                    {
                        attendanceId=session.Id,assignmentId=assignment.Id,volunteerUserId=assignment.VolunteerId,
                        session.CheckInAt,thresholdMinutes=options.ThresholdMinutes,leaderUserId=area.LeaderUserId
                    });
                    await attendance.SaveAsync(ct);
                    return true;
                },ct);
            }
            after=batch[^1].SessionId;
        }
    }
}
