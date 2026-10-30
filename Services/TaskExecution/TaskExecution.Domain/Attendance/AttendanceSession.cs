namespace TaskExecution.Domain.Attendance;

public enum AttendanceStatus { Open, Closed, Corrected }

public sealed class AttendanceSession
{
    private AttendanceSession() { }
    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid CheckInRequestId { get; private set; }
    public DateTimeOffset CheckInAt { get; private set; }
    public DateTimeOffset? CheckOutAt { get; private set; }
    public decimal? WorkingHours { get; private set; }
    public int ApprovedMinutes { get; private set; }
    public AttendanceStatus Status { get; private set; }
    public int Version { get; private set; } = 1;

    public static AttendanceSession Open(AttendanceRequest request)
    {
        if (request.Status!=AttendanceRequestStatus.Approved || !request.AttendanceId.HasValue)
            throw new DomainRuleException("ATTENDANCE_REQUEST_NOT_APPROVED","An approved check-in request is required.");
        return new AttendanceSession
        {
            Id=request.AttendanceId.Value,AssignmentId=request.AssignmentId,CheckInRequestId=request.Id,
            CheckInAt=request.RequestedAt,Status=AttendanceStatus.Open
        };
    }

    public void Close(DateTimeOffset serverTime,int? expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status!=AttendanceStatus.Open)
            throw new DomainRuleException("ATTENDANCE_NOT_OPEN","Only an open session can check out.");
        SetApprovedTimes(CheckInAt,serverTime);
        Status=AttendanceStatus.Closed;
        Version++;
    }

    public void EnsureVersion(int? version)
    {
        if (version.HasValue && version!=Version)
            throw new DomainRuleException("ATTENDANCE_VERSION_MISMATCH","The session changed.",DomainErrorKind.PreconditionFailed);
    }

    public void Correct(DateTimeOffset start,DateTimeOffset end,int expectedVersion)
    {
        EnsureVersion(expectedVersion);
        SetApprovedTimes(start,end);
        Status=AttendanceStatus.Corrected;
        Version++;
    }

    private void SetApprovedTimes(DateTimeOffset start,DateTimeOffset end)
    {
        if (end<=start || (end-start).TotalMinutes>599999)
            throw DomainRuleException.Validation("ATTENDANCE_TIME_INVALID","Check-out must follow check-in and duration must fit the working-hours limit.");
        CheckInAt=start.ToUniversalTime();CheckOutAt=end.ToUniversalTime();
        ApprovedMinutes=(int)Math.Floor((end-start).TotalMinutes);
        WorkingHours=decimal.Round(ApprovedMinutes/60m,2,MidpointRounding.AwayFromZero);
    }
}
