namespace TaskExecution.Domain.Attendance;

public enum AttendanceRequestStatus { Pending, Approved, Rejected, Cancelled }

public sealed class AttendanceRequest
{
    private AttendanceRequest() { }
    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid VolunteerId { get; private set; }
    public Guid? AttendanceId { get; private set; }
    public string Type { get; private set; } = "CHECK_IN";
    public DateTimeOffset RequestedAt { get; private set; }
    public AttendanceRequestStatus Status { get; private set; }
    public string? Note { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public string? ReviewNote { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public int Version { get; private set; } = 1;

    public static AttendanceRequest CheckIn(Guid assignmentId, Guid volunteerId, string? note, DateTimeOffset serverTime)
    {
        if (assignmentId == Guid.Empty || volunteerId == Guid.Empty)
            throw DomainRuleException.Validation("ATTENDANCE_IDENTITY_INVALID", "Assignment and volunteer are required.");
        if (note?.Length > 2000)
            throw DomainRuleException.Validation("ATTENDANCE_NOTE_INVALID", "Note must not exceed 2000 characters.");
        return new AttendanceRequest
        {
            Id = Guid.NewGuid(), AssignmentId = assignmentId, VolunteerId = volunteerId,
            RequestedAt = serverTime.ToUniversalTime(), Status = AttendanceRequestStatus.Pending,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
    }

    public void EnsurePending(int? version)
    {
        if (version.HasValue && version != Version)
            throw new DomainRuleException("ATTENDANCE_VERSION_MISMATCH", "The request changed.", DomainErrorKind.PreconditionFailed);
        if (Status != AttendanceRequestStatus.Pending)
            throw new DomainRuleException("ATTENDANCE_REQUEST_INVALID_STATE", "Only a pending request can be changed.");
    }

    public void Cancel(int? version)
    {
        EnsurePending(version);
        Status = AttendanceRequestStatus.Cancelled;
        Version++;
    }

    public void Review(bool approve,Guid reviewerId,string? note,Guid? attendanceId,int? version,DateTimeOffset now)
    {
        EnsurePending(version);
        var normalized=note?.Trim();
        if ((!approve && string.IsNullOrWhiteSpace(normalized)) || normalized?.Length>2000)
            throw DomainRuleException.Validation("ATTENDANCE_REVIEW_NOTE_REQUIRED","Rejection requires a review note (maximum 2000 characters).");
        if (approve && (!attendanceId.HasValue || attendanceId==Guid.Empty))
            throw DomainRuleException.Validation("ATTENDANCE_IDENTITY_INVALID","Approval requires an attendance session ID.");
        Status=approve ? AttendanceRequestStatus.Approved : AttendanceRequestStatus.Rejected;
        AttendanceId=approve ? attendanceId : null;
        ReviewedBy=reviewerId; ReviewNote=normalized; ReviewedAt=now.ToUniversalTime(); Version++;
    }
}
