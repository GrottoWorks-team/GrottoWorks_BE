namespace Reporting.Domain.ReadModels;

/// <summary><c>rm_attendance</c> (read model): approved hours of one attendance (BR-21), from <c>attendance.hours.approved</c>.</summary>
public sealed class AttendanceView
{
    private AttendanceView()
    {
    }

    public Guid AttendanceId { get; private set; }

    public Guid VolunteerUserId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid ParishId { get; private set; }

    public decimal ApprovedHours { get; private set; }

    /// <summary>Optimistic concurrency token (<c>xmin</c>): concurrent events on the same row → one is redelivered.</summary>
    public uint Version { get; private set; }

    public DateTimeOffset ApprovedAt { get; private set; }

    public static AttendanceView Create(Guid attendanceId, Guid volunteerUserId, Guid taskId, Guid workAreaId, Guid seasonId, Guid parishId) =>
        new()
        {
            AttendanceId = attendanceId,
            VolunteerUserId = volunteerUserId,
            TaskId = taskId,
            WorkAreaId = workAreaId,
            SeasonId = seasonId,
            ParishId = parishId
        };

    /// <summary>A corrected approval replaces the hours (latest approval wins).</summary>
    public void Approve(decimal hours, DateTimeOffset at)
    {
        if (at.ToUniversalTime() < ApprovedAt)
        {
            return;
        }

        ApprovedHours = hours;
        ApprovedAt = at.ToUniversalTime();
    }
}
