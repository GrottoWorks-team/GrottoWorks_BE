using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.Application.Attendance;

public sealed record AttendanceCorrectionDto(Guid Id,Guid AttendanceId,Guid RequestedBy,Guid? ReviewedBy,
    DateTimeOffset OriginalCheckInAt,DateTimeOffset? OriginalCheckOutAt,DateTimeOffset RequestedCheckInAt,
    DateTimeOffset RequestedCheckOutAt,string Reason,AttendanceCorrectionStatus Status,string? ReviewNote,
    DateTimeOffset CreatedAt,DateTimeOffset? ReviewedAt,int Version)
{
    public static AttendanceCorrectionDto From(AttendanceCorrection row)=>new(row.Id,row.AttendanceId,row.RequestedBy,row.ReviewedBy,
        row.OriginalCheckInAt,row.OriginalCheckOutAt,row.RequestedCheckInAt,row.RequestedCheckOutAt,row.Reason,
        row.Status,row.ReviewNote,row.CreatedAt,row.ReviewedAt,row.Version);
}

public sealed partial class AttendanceService
{
    public async Task<AttendanceCorrectionDto> RequestCorrectionAsync(Guid sessionId,DateTimeOffset start,DateTimeOffset end,
        string reason,int? sessionVersion,CancellationToken cancellationToken=default)
    {
        return await store.ForVolunteerAsync(user.UserId,async () =>
        {
            await EnsureCallerActiveAsync(cancellationToken);
            var session=await store.SessionAsync(sessionId,cancellationToken)
                ?? throw DomainRuleException.NotFound("ATTENDANCE_NOT_FOUND","The session does not exist.");
            var (_,task,_)=await VolunteerAssignmentAsync(session.AssignmentId,cancellationToken);
            session.EnsureVersion(sessionVersion);
            if (await store.HasPendingCorrectionAsync(sessionId,cancellationToken))
                throw new DomainRuleException("ATTENDANCE_CORRECTION_PENDING","This session already has a pending correction.");
            var correction=AttendanceCorrection.Request(session,user.UserId,start,end,reason,DateTimeOffset.UtcNow);
            if (await store.OverlapsAsync(user.UserId,start,end,sessionId,cancellationToken))
                throw new DomainRuleException("ATTENDANCE_OVERLAP","The corrected range overlaps another session.");
            store.Add(correction);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            await store.SaveAsync(cancellationToken);
            return AttendanceCorrectionDto.From(correction);
        },cancellationToken);
    }

    public async Task<(IReadOnlyList<AttendanceCorrectionDto> Items,long Total)> CorrectionsAsync(Guid areaId,AttendanceCorrectionStatus status,
        int page,int size,bool descending,CancellationToken cancellationToken=default)
    {
        await scopes.GetWorkAreaForLeaderReadAsync(areaId,cancellationToken);
        var result=await store.CorrectionsPageAsync(areaId,status,page,size,descending,cancellationToken);
        return (result.Items.Select(AttendanceCorrectionDto.From).ToList(),result.Total);
    }
}
