namespace TaskExecution.Domain.Attendance;

public enum AttendanceCorrectionStatus { Pending, Approved, Rejected }

public sealed class AttendanceCorrection
{
    private AttendanceCorrection() { }
    public Guid Id {get;private set;}
    public Guid AttendanceId {get;private set;}
    public Guid RequestedBy {get;private set;}
    public Guid? ReviewedBy {get;private set;}
    public DateTimeOffset OriginalCheckInAt {get;private set;}
    public DateTimeOffset? OriginalCheckOutAt {get;private set;}
    public DateTimeOffset RequestedCheckInAt {get;private set;}
    public DateTimeOffset RequestedCheckOutAt {get;private set;}
    public int SessionVersion {get;private set;}
    public string Reason {get;private set;}="";
    public AttendanceCorrectionStatus Status {get;private set;}
    public string? ReviewNote {get;private set;}
    public DateTimeOffset? ReviewedAt {get;private set;}
    public DateTimeOffset CreatedAt {get;private set;}
    public int Version {get;private set;}=1;

    public static AttendanceCorrection Request(AttendanceSession session,Guid volunteer,DateTimeOffset start,DateTimeOffset end,string reason,DateTimeOffset now)
    {
        var normalized=reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length>2000)
            throw DomainRuleException.Validation("ATTENDANCE_CORRECTION_REASON_REQUIRED","Reason must contain 1–2000 characters.");
        if (end<=start || end>now || (end-start).TotalMinutes>599999)
            throw DomainRuleException.Validation("ATTENDANCE_TIME_INVALID","Corrected times must form a positive range ending no later than server time.");
        return new AttendanceCorrection
        {
            Id=Guid.NewGuid(),AttendanceId=session.Id,RequestedBy=volunteer,
            OriginalCheckInAt=session.CheckInAt,OriginalCheckOutAt=session.CheckOutAt,SessionVersion=session.Version,
            RequestedCheckInAt=start.ToUniversalTime(),RequestedCheckOutAt=end.ToUniversalTime(),Reason=normalized,
            Status=AttendanceCorrectionStatus.Pending,CreatedAt=now.ToUniversalTime()
        };
    }

    public void EnsureReviewAllowed(bool approve,string? note,int? version)
    {
        if (version.HasValue && version!=Version)
            throw new DomainRuleException("ATTENDANCE_VERSION_MISMATCH","The correction changed.",DomainErrorKind.PreconditionFailed);
        if (Status!=AttendanceCorrectionStatus.Pending)
            throw new DomainRuleException("ATTENDANCE_CORRECTION_INVALID_STATE","Only a pending correction can be reviewed.");
        if ((!approve && string.IsNullOrWhiteSpace(note)) || note?.Trim().Length>2000)
            throw DomainRuleException.Validation("ATTENDANCE_REVIEW_NOTE_REQUIRED","Rejection requires a note (maximum 2000 characters).");
    }

    public void Review(bool approve,Guid reviewer,string? note,int? version,DateTimeOffset now)
    {
        EnsureReviewAllowed(approve,note,version);
        Status=approve ? AttendanceCorrectionStatus.Approved : AttendanceCorrectionStatus.Rejected;
        ReviewedBy=reviewer;ReviewNote=string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ReviewedAt=now.ToUniversalTime();Version++;
    }
}
