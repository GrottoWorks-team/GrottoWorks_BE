using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.UnitTests.Application;

public sealed class AttendanceCheckOutTests
{
    internal static AttendanceSession OpenSession(Guid assignment,Guid volunteer,DateTimeOffset start)
    {
        var request=AttendanceRequest.CheckIn(assignment,volunteer,null,start);
        request.Review(true,Guid.NewGuid(),null,Guid.NewGuid(),1,start);
        return AttendanceSession.Open(request);
    }

    [Fact]
    public async Task Check_out_is_direct_and_retry_does_not_add_hours_twice()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var session=OpenSession(id,f.VolunteerId,DateTimeOffset.UtcNow.AddMinutes(-60).AddSeconds(-5));store.Add(session);
        var service=f.Attendance(store);var key=Guid.NewGuid();
        var result=await service.CheckOutAsync(id,1,key);
        result.ApprovedMinutes.Should().Be(60);result.Status.Should().Be(AttendanceStatus.Closed);
        (await service.CheckOutAsync(id,1,key)).Should().Be(result);
        store.Saves.Should().Be(1);
        f.Events.Items.Count(e=>e.EventType=="attendance.hours.approved").Should().Be(1);
    }

    [Fact]
    public void Session_requires_positive_time_and_preserves_exact_approved_minutes()
    {
        var start=DateTimeOffset.UtcNow;
        var session=OpenSession(Guid.NewGuid(),Guid.NewGuid(),start);
        var act=()=>session.Close(start,1);act.Should().Throw<DomainRuleException>();
        session.Close(start.AddMinutes(91).AddSeconds(30),1);
        session.ApprovedMinutes.Should().Be(91);session.WorkingHours.Should().Be(1.52m);
        act=()=>session.Close(start.AddHours(3),2);act.Should().Throw<DomainRuleException>();
    }

    [Fact]
    public async Task Missing_open_session_cannot_check_out()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();
        var act=()=>f.Attendance(new AttendanceTestStore(f)).CheckOutAsync(id,null,Guid.NewGuid());
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="ATTENDANCE_NOT_OPEN");
    }
}
