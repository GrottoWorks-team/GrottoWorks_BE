using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;
using TaskState=TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class AttendanceReviewTests
{
    [Fact]
    public async Task Late_approval_uses_requested_time_and_starts_the_ready_task()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var request=AttendanceRequest.CheckIn(id,f.VolunteerId,null,DateTimeOffset.UtcNow.AddHours(-2));store.Add(request);
        f.User.UserId=f.Area.LeaderUserId!.Value;f.User.Role="LEADER";
        var result=await f.Attendance(store).ReviewRequestAsync(request.Id,true,null,1);
        result.Status.Should().Be(AttendanceRequestStatus.Approved);
        store.Sessions.Single().CheckInAt.Should().Be(request.RequestedAt);
        f.Task.Status.Should().Be(TaskState.InProgress);
        store.Sessions.Single().ApprovedMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Rejection_requires_a_note_and_never_creates_hours()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var request=AttendanceRequest.CheckIn(id,f.VolunteerId,null,DateTimeOffset.UtcNow);store.Add(request);
        f.User.UserId=f.Area.LeaderUserId!.Value;f.User.Role="LEADER";
        var service=f.Attendance(store);var act=()=>service.ReviewRequestAsync(request.Id,false," ",1);
        await act.Should().ThrowAsync<DomainRuleException>();
        var result=await service.ReviewRequestAsync(request.Id,false," unavailable ",1);
        result.Status.Should().Be(AttendanceRequestStatus.Rejected);result.ReviewNote.Should().Be("unavailable");
        store.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_open_session_prevents_a_second_approval()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var old=AttendanceRequest.CheckIn(id,f.VolunteerId,null,DateTimeOffset.UtcNow.AddHours(-3));
        old.Review(true,f.Area.LeaderUserId!.Value,null,Guid.NewGuid(),1,DateTimeOffset.UtcNow);store.Add(AttendanceSession.Open(old));
        var request=AttendanceRequest.CheckIn(id,f.VolunteerId,null,DateTimeOffset.UtcNow);store.Add(request);
        f.User.UserId=f.Area.LeaderUserId.Value;f.User.Role="LEADER";
        var act=()=>f.Attendance(store).ReviewRequestAsync(request.Id,true,null,1);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="ATTENDANCE_OVERLAP");
        request.Status.Should().Be(AttendanceRequestStatus.Pending);
    }
}
