using FluentAssertions;
using TaskExecution.Application.Attendance;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.UnitTests.Application;

public sealed class LongOpenAttendanceTests
{
    [Fact]
    public async Task Repeated_scan_records_one_pending_event_per_session_and_threshold()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var session=AttendanceCheckOutTests.OpenSession(id,f.VolunteerId,DateTimeOffset.UtcNow.AddHours(-13));store.Add(session);
        var alerts=new AlertStore(session,f.VolunteerId);
        var processor=f.Alerts(alerts,store);
        await processor.ScanAsync(DateTimeOffset.UtcNow);await processor.ScanAsync(DateTimeOffset.UtcNow);
        alerts.Markers.Should().ContainSingle();
        f.Events.Items.Count(e=>e.EventType=="attendance.session.open-too-long").Should().Be(1);
        session.CheckOutAt.Should().BeNull();session.ApprovedMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Leader_query_uses_own_work_area_scope_while_admin_can_query_all()
    {
        var f=new AssignmentTestFixture();var session=AttendanceCheckOutTests.OpenSession(Guid.NewGuid(),f.VolunteerId,DateTimeOffset.UtcNow.AddHours(-13));
        var store=new AlertStore(session,f.VolunteerId);var query=new LongOpenAttendanceQuery(store,new(),f.User);
        await query.HandleAsync(null);store.Leader.Should().Be(f.User.UserId);store.Parish.Should().Be(f.User.ParishId);
        f.User.Role="ADMIN";await query.HandleAsync(null);store.Leader.Should().BeNull();store.Parish.Should().BeNull();
    }

    private sealed class AlertStore(AttendanceSession session,Guid volunteer) : ILongOpenAttendanceStore
    {
        public List<AttendanceSessionAlert> Markers {get;}=[];public Guid? Leader,Parish;
        public Task<IReadOnlyList<AttendanceSession>> ListAsync(DateTimeOffset cutoff,Guid? parish,Guid? leader,CancellationToken ct)
        {Leader=leader;Parish=parish;return Task.FromResult<IReadOnlyList<AttendanceSession>>([session]);}
        public Task<IReadOnlyList<AttendanceAlertCandidate>> CandidatesAsync(DateTimeOffset cutoff,Guid? after,int size,CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<AttendanceAlertCandidate>>(after.HasValue ? [] : [new(session.Id,volunteer)]);
        public Task<bool> HasAlertAsync(Guid id,int threshold,CancellationToken ct)=>Task.FromResult(Markers.Any(m=>m.AttendanceId==id && m.ThresholdMinutes==threshold));
        public void Add(AttendanceSessionAlert row)=>Markers.Add(row);
    }
}
