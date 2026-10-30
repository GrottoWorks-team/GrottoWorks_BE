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

    public static AttendanceSession Open(AttendanceRequest request) => new()
    {
        Id = Guid.NewGuid(), AssignmentId = request.AssignmentId, CheckInRequestId = request.Id,
        CheckInAt = request.RequestedAt, Status = AttendanceStatus.Open
    };
}
