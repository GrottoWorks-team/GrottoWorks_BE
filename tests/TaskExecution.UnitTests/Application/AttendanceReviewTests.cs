using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;
using TaskExecution.Domain.Tasks;
using TaskState=TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class AttendanceReviewTests
{
    [Fact]
    public async Task A_second_attendance_session_does_not_restart_the_task()
    {
        var (fixture, assignmentId) = await CheckInRequestTests.ReadyVolunteerAsync();
        var startedAt = fixture.Task.StartedAt;
        var store = new AttendanceTestStore(fixture);
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
        var earlierSession = AttendanceCheckOutTests.OpenSession(assignmentId, fixture.VolunteerId, yesterday);
        earlierSession.Close(yesterday.AddHours(1), earlierSession.Version);
        store.Add(earlierSession);

        var service = fixture.Attendance(store);
        var request = await service.CheckInAsync(assignmentId, null, Guid.NewGuid());
        fixture.User.UserId = fixture.Area.LeaderUserId!.Value;
        fixture.User.Role = "LEADER";
        await service.ReviewRequestAsync(request.Id, true, null, request.Version);

        store.Sessions.Should().HaveCount(2);
        fixture.Task.Status.Should().Be(TaskState.InProgress);
        fixture.Task.StartedAt.Should().Be(startedAt);
        fixture.Events.Items.Should().NotContain(message => message.EventType == "task.status.changed");
    }

    [Fact]
    public async Task Late_approval_uses_requested_time_without_emitting_a_task_transition()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var request=AttendanceRequest.CheckIn(id,f.VolunteerId,null,DateTimeOffset.UtcNow.AddHours(-2));store.Add(request);
        f.User.UserId=f.Area.LeaderUserId!.Value;f.User.Role="LEADER";
        var result=await f.Attendance(store).ReviewRequestAsync(request.Id,true,null,1);
        result.Status.Should().Be(AttendanceRequestStatus.Approved);
        store.Sessions.Single().CheckInAt.Should().Be(request.RequestedAt);
        f.Task.Status.Should().Be(TaskState.InProgress);
        f.Events.Items.Should().NotContain(message => message.EventType == "task.status.changed");
        store.Sessions.Single().ApprovedMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Approval_before_start_keeps_the_request_pending()
    {
        var (fixture, assignmentId) = await CheckInRequestTests.ReadyVolunteerAsync(started: false);
        var store = new AttendanceTestStore(fixture);
        var request = AttendanceRequest.CheckIn(assignmentId, fixture.VolunteerId, null, DateTimeOffset.UtcNow);
        store.Add(request);
        fixture.User.UserId = fixture.Area.LeaderUserId!.Value;
        fixture.User.Role = "LEADER";
        var action = () => fixture.Attendance(store).ReviewRequestAsync(request.Id, true, null, 1);
        await action.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "TASK_INVALID_STATE");
        request.Status.Should().Be(AttendanceRequestStatus.Pending);
        store.Sessions.Should().BeEmpty();
        fixture.Task.Status.Should().Be(TaskState.NotStarted);
    }

    [Fact]
    public async Task First_purchase_check_in_still_starts_the_task()
    {
        var (fixture, assignmentId) = await CheckInRequestTests.ReadyVolunteerAsync(started: false, type: TaskType.Purchase);
        var store = new AttendanceTestStore(fixture);
        var service = fixture.Attendance(store);
        var request = await service.CheckInAsync(assignmentId, null, Guid.NewGuid());
        fixture.User.UserId = fixture.Area.LeaderUserId!.Value;
        fixture.User.Role = "LEADER";
        await service.ReviewRequestAsync(request.Id, true, null, request.Version);
        fixture.Task.Status.Should().Be(TaskState.InProgress);
        fixture.Task.StartedAt.Should().BeNull();
        fixture.Events.Items.Should().ContainSingle(message => message.EventType == "task.status.changed");
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
