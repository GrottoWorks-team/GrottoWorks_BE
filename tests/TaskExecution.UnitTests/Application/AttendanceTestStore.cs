using TaskExecution.Application.Attendance;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.UnitTests.Application;

internal sealed partial class AttendanceTestStore(AssignmentTestFixture fixture) : IAttendanceStore
{
    public List<AttendanceRequest> Requests { get; }=[];
    public List<AttendanceSession> Sessions { get; }=[];
    public List<AttendanceReplay> Replays { get; }=[];
    public int Saves { get; private set; }
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task<T> ForVolunteerAsync<T>(Guid id,Func<Task<T>> action,CancellationToken ct)
    { await gate.WaitAsync(ct); try { return await action(); } finally { gate.Release(); } }
    public Task<AttendanceRequest?> RequestAsync(Guid id,CancellationToken ct)=>Task.FromResult(Requests.SingleOrDefault(r=>r.Id==id));
    public Task<AttendanceSession?> SessionAsync(Guid id,CancellationToken ct)=>Task.FromResult(Sessions.SingleOrDefault(r=>r.Id==id));
    public Task<bool> HasPendingOrOpenAsync(Guid id,CancellationToken ct)=>Task.FromResult(
        Requests.Any(r=>r.VolunteerId==id && r.Status==AttendanceRequestStatus.Pending) ||
        Sessions.Any(s=>!s.CheckOutAt.HasValue && fixture.Store.Items.Any(a=>a.Id==s.AssignmentId && a.VolunteerId==id)));
    public Task<bool> OverlapsAsync(Guid id,DateTimeOffset start,DateTimeOffset? end,Guid? exclude,CancellationToken ct)=>Task.FromResult(
        Sessions.Any(s=>s.Id!=exclude && (!end.HasValue || s.CheckInAt<end) && (!s.CheckOutAt.HasValue || s.CheckOutAt>start) &&
            fixture.Store.Items.Any(a=>a.Id==s.AssignmentId && a.VolunteerId==id)));
    public Task<AttendanceReplay?> ReplayAsync(Guid id,Guid key,CancellationToken ct)=>Task.FromResult(Replays.SingleOrDefault(r=>r.ActorId==id && r.Key==key));
    public void Add(AttendanceRequest r)=>Requests.Add(r);
    public void Add(AttendanceSession s)=>Sessions.Add(s);
    public void Add(AttendanceReplay r)=>Replays.Add(r);
    public Task SaveAsync(CancellationToken ct) { Saves++;return Task.CompletedTask; }
}
