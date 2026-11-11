using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.UnitTests.Application;

public sealed class AttendanceSessionCloseTests
{
    [Fact]
    public async Task Leader_closes_open_session_and_hours_are_approved()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var session=AttendanceCheckOutTests.OpenSession(id,f.VolunteerId,DateTimeOffset.UtcNow.AddHours(-14));store.Add(session);
        f.User.UserId=f.Area.LeaderUserId!.Value;f.User.Role="LEADER";
        var result=await f.Attendance(store).CloseSessionAsync(session.Id,"Tình nguyện viên quên check-out",session.Version);
        result.Status.Should().Be(AttendanceStatus.Closed);
        result.CheckOutAt.Should().NotBeNull();
        result.ApprovedMinutes.Should().BeGreaterThan(0);
        f.Events.Items.Should().Contain(e=>e.EventType=="attendance.session.closed-by-leader");
    }

    [Fact]
    public async Task Reason_is_required()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var session=AttendanceCheckOutTests.OpenSession(id,f.VolunteerId,DateTimeOffset.UtcNow.AddHours(-14));store.Add(session);
        f.User.UserId=f.Area.LeaderUserId!.Value;f.User.Role="LEADER";
        var act=()=>f.Attendance(store).CloseSessionAsync(session.Id,"  ",session.Version);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="CLOSE_REASON_REQUIRED");
        session.CheckOutAt.Should().BeNull();
    }

    [Fact]
    public async Task Closed_session_cannot_be_closed_twice()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var session=AttendanceCheckOutTests.OpenSession(id,f.VolunteerId,DateTimeOffset.UtcNow.AddHours(-14));store.Add(session);
        f.User.UserId=f.Area.LeaderUserId!.Value;f.User.Role="LEADER";
        var service=f.Attendance(store);
        var first=await service.CloseSessionAsync(session.Id,"Quên check-out",session.Version);
        var act=()=>service.CloseSessionAsync(session.Id,"Lần hai",first.Version);
        await act.Should().ThrowAsync<DomainRuleException>();
    }
}
