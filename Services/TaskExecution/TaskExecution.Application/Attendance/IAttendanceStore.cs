using TaskExecution.Domain.Attendance;

namespace TaskExecution.Application.Attendance;

public sealed class AttendanceReplay
{
    private AttendanceReplay() { }
    public Guid ActorId { get; private set; }
    public Guid Key { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string Result { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public static AttendanceReplay Create(Guid actor, Guid key, string fingerprint, string result) => new()
    { ActorId=actor, Key=key, Fingerprint=fingerprint, Result=result, CreatedAt=DateTimeOffset.UtcNow };
}

public interface IAttendanceStore
{
    Task<T> ForVolunteerAsync<T>(Guid volunteerId, Func<Task<T>> action, CancellationToken cancellationToken);
    Task<AttendanceRequest?> RequestAsync(Guid id, CancellationToken cancellationToken);
    Task<AttendanceSession?> SessionAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasPendingOrOpenAsync(Guid volunteerId, CancellationToken cancellationToken);
    Task<bool> OverlapsAsync(Guid volunteerId, DateTimeOffset start, DateTimeOffset? end, Guid? excludeSession, CancellationToken cancellationToken);
    Task<AttendanceReplay?> ReplayAsync(Guid actorId, Guid key, CancellationToken cancellationToken);
    void Add(AttendanceRequest request);
    void Add(AttendanceSession session);
    void Add(AttendanceReplay replay);
    Task SaveAsync(CancellationToken cancellationToken);
}
