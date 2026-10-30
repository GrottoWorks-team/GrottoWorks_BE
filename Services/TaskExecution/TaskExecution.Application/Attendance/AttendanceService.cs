using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Security;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Attendance;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Attendance;

public sealed record AttendanceRequestDto(Guid Id, Guid AssignmentId, string Type, DateTimeOffset RequestedAt,
    AttendanceRequestStatus Status, string? Note, Guid? AttendanceId, Guid? ReviewedBy, string? ReviewNote, DateTimeOffset? ReviewedAt, int Version)
{
    public static AttendanceRequestDto From(AttendanceRequest row) => new(row.Id,row.AssignmentId,row.Type,row.RequestedAt,
        row.Status,row.Note,row.AttendanceId,row.ReviewedBy,row.ReviewNote,row.ReviewedAt,row.Version);
}

public sealed record AttendanceDto(Guid Id, Guid AssignmentId, DateTimeOffset CheckInAt, DateTimeOffset? CheckOutAt,
    int ApprovedMinutes, AttendanceStatus Status, int Version)
{
    public static AttendanceDto From(AttendanceSession row) => new(row.Id,row.AssignmentId,row.CheckInAt,row.CheckOutAt,row.ApprovedMinutes,row.Status,row.Version);
}

public sealed partial class AttendanceService(IAttendanceStore store, IAssignmentStore assignments, ITaskRepository tasks,
    TaskScopeAccess scopes, ActiveUserAccess users, ICurrentUser user, TaskEventRecorder events)
{
    public async Task<AttendanceRequestDto> CheckInAsync(Guid assignmentId, string? note, Guid key, CancellationToken cancellationToken = default)
    {
        if (key == Guid.Empty) throw DomainRuleException.Validation("IDEMPOTENCY_KEY_REQUIRED","A UUID Idempotency-Key is required.");
        return await store.ForVolunteerAsync(user.UserId,async () =>
        {
            await EnsureCallerActiveAsync(cancellationToken);
            var fingerprint = Fingerprint(new { operation="CHECK_IN",assignmentId,note });
            var cached = await store.ReplayAsync(user.UserId,key,cancellationToken);
            if (cached is not null) return Replay<AttendanceRequestDto>(cached,fingerprint);
            var (assignment,task,area) = await VolunteerAssignmentAsync(assignmentId,cancellationToken);
            EnsureTaskAllowsCheckIn(task);
            if (assignment.Status != AssignmentStatus.Accepted)
                throw new DomainRuleException("ASSIGNMENT_NOT_ACCEPTED","Only accepted assignments can check in.");
            if (await store.HasPendingOrOpenAsync(user.UserId,cancellationToken))
                throw new DomainRuleException("ATTENDANCE_ALREADY_OPEN","A pending request or open session already exists.");
            var row = AttendanceRequest.CheckIn(assignmentId,user.UserId,note,DateTimeOffset.UtcNow);
            store.Add(row);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            events.Record(TaskEventTypes.AttendanceRequested,task,area,new { requestId=row.Id,assignmentId,volunteerUserId=user.UserId,row.RequestedAt });
            var result = AttendanceRequestDto.From(row);
            store.Add(AttendanceReplay.Create(user.UserId,key,fingerprint,JsonSerializer.Serialize(result)));
            await store.SaveAsync(cancellationToken);
            return result;
        },cancellationToken);
    }

    public async Task<AttendanceRequestDto> CancelRequestAsync(Guid requestId,int? version,CancellationToken cancellationToken=default)
    {
        return await store.ForVolunteerAsync(user.UserId,async () =>
        {
            await EnsureCallerActiveAsync(cancellationToken);
            var request = await store.RequestAsync(requestId,cancellationToken)
                ?? throw DomainRuleException.NotFound("ATTENDANCE_REQUEST_NOT_FOUND","The request does not exist.");
            if (request.VolunteerId != user.UserId)
                throw DomainRuleException.Forbidden("ATTENDANCE_ACCESS_DENIED","Only the requesting volunteer can cancel.");
            var (_,task,area) = await VolunteerAssignmentAsync(request.AssignmentId,cancellationToken);
            request.Cancel(version);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            events.Record(TaskEventTypes.AttendanceReviewed,task,area,new { requestId,decision="CANCELLED",volunteerUserId=user.UserId });
            await store.SaveAsync(cancellationToken);
            return AttendanceRequestDto.From(request);
        },cancellationToken);
    }

    private async Task EnsureCallerActiveAsync(CancellationToken ct)
    {
        if (user.Role != GrottoWorksRoles.Volunteer || !user.ParishId.HasValue)
            throw DomainRuleException.Forbidden("ATTENDANCE_ACCESS_DENIED","A volunteer parish is required.");
        await users.EnsureCallerAsync(user.ParishId.Value,ct);
    }

    private async Task<(TaskAssignment Assignment,WorkTask Task,WorkAreaScope Area)> VolunteerAssignmentAsync(Guid id,CancellationToken ct)
    {
        var assignment = await assignments.GetAsync(id,ct)
            ?? throw DomainRuleException.NotFound("TASK_ASSIGNMENT_NOT_FOUND","The assignment does not exist.");
        if (assignment.VolunteerId != user.UserId)
            throw DomainRuleException.Forbidden("ATTENDANCE_ACCESS_DENIED","The assignment belongs to another volunteer.");
        var task = await tasks.GetByIdForUpdateAsync(assignment.TaskId,ct)
            ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND","The task does not exist.");
        var (area,_) = await scopes.GetWorkAreaForVolunteerWriteAsync(task.WorkAreaId,ct);
        return (assignment,task,area);
    }

    private static void EnsureTaskAllowsCheckIn(WorkTask task)
    {
        if (task.Status is not (TaskState.NotStarted or TaskState.InProgress))
            throw new DomainRuleException("TASK_INVALID_STATE","The task must be ready and not yet submitted or completed.");
    }

    public static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static T Replay<T>(AttendanceReplay replay,string fingerprint)
    {
        if (replay.Fingerprint != fingerprint)
            throw new DomainRuleException("IDEMPOTENCY_KEY_CONFLICT","The key was already used with a different request.");
        return JsonSerializer.Deserialize<T>(replay.Result)!;
    }
}
