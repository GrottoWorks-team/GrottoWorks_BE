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
    Task<IReadOnlyList<AttendanceSession>> ListAsync(DateTimeOffset cutoff,Guid? parishId,Guid? leaderId,CancellationToken ct);
    Task<IReadOnlyList<AttendanceAlertCandidate>> CandidatesAsync(DateTimeOffset cutoff,Guid? after,int size,CancellationToken ct);
    Task<bool> HasAlertAsync(Guid sessionId,int thresholdMinutes,CancellationToken ct);
    void Add(AttendanceSessionAlert alert);
}

public sealed class LongOpenAttendanceQuery(ILongOpenAttendanceStore store,LongOpenAttendanceOptions options,ICurrentUser user)
{
    public async Task<IReadOnlyList<AttendanceDto>> HandleAsync(int? hours,CancellationToken ct=default)
    {
        if (hours is <=0 or >9999)
            throw new DomainRuleException("INVALID_DURATION","Hours must be between 1 and 9999.",DomainErrorKind.BadRequest);
        if (user.Role is not (GrottoWorksRoles.Leader or GrottoWorksRoles.Admin))
            throw DomainRuleException.Forbidden("ATTENDANCE_ACCESS_DENIED","Leader or admin access is required.");
        if (user.Role==GrottoWorksRoles.Leader && !user.ParishId.HasValue)
            throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED","A leader parish is required.");
        var cutoff=DateTimeOffset.UtcNow.AddMinutes(-(hours*60 ?? options.ThresholdMinutes));
        var rows=await store.ListAsync(cutoff,user.Role==GrottoWorksRoles.Admin ? null : user.ParishId,
            user.Role==GrottoWorksRoles.Admin ? null : user.UserId,ct);
        return rows.Select(AttendanceDto.From).ToList();
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
