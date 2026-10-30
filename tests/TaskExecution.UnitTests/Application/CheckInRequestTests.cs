using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.UnitTests.Application;

public sealed class CheckInRequestTests
{
    internal static async Task<(AssignmentTestFixture Fixture,Guid Assignment)> ReadyVolunteerAsync()
    {
        var f=new AssignmentTestFixture();
        var row=await f.Service.AssignAsync(f.Command());
        f.Store.Items.Single().Accept(1,DateTimeOffset.UtcNow);
        f.Task.MarkReady(f.Task.Version,DateTimeOffset.UtcNow);
        f.User.UserId=f.VolunteerId;f.User.Role="VOLUNTEER";
        return (f,row.Id);
    }

    [Fact]
    public async Task Retry_replays_the_same_request_without_new_event_or_save()
    {
        var (f,id)=await ReadyVolunteerAsync();
        var store=new AttendanceTestStore(f);var service=f.Attendance(store);var key=Guid.NewGuid();
        var before=DateTimeOffset.UtcNow;
        var first=await service.CheckInAsync(id,"here",key);
        var second=await service.CheckInAsync(id,"here",key);
        second.Should().Be(first);
        first.RequestedAt.Should().BeOnOrAfter(before);
        store.Requests.Should().ContainSingle();store.Saves.Should().Be(1);
        store.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Reusing_key_with_different_payload_and_second_pending_request_are_rejected()
    {
        var (f,id)=await ReadyVolunteerAsync();var store=new AttendanceTestStore(f);var service=f.Attendance(store);var key=Guid.NewGuid();
        await service.CheckInAsync(id,null,key);
        var act=()=>service.CheckInAsync(id,"changed",key);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="IDEMPOTENCY_KEY_CONFLICT");
        act=()=>service.CheckInAsync(id,null,Guid.NewGuid());
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="ATTENDANCE_ALREADY_OPEN");
    }

    [Fact]
    public async Task Cancellation_keeps_request_history_and_releases_the_pending_request()
    {
        var (f,id)=await ReadyVolunteerAsync();var store=new AttendanceTestStore(f);var service=f.Attendance(store);
        var request=await service.CheckInAsync(id,null,Guid.NewGuid());
        var result=await service.CancelRequestAsync(request.Id,1);
        result.Status.Should().Be(AttendanceRequestStatus.Cancelled);result.Version.Should().Be(2);
        await service.CheckInAsync(id,null,Guid.NewGuid());
        store.Requests.Should().HaveCount(2);store.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_task_cannot_skip_ready_by_checking_in()
    {
        var f=new AssignmentTestFixture();var row=await f.Service.AssignAsync(f.Command());
        f.Store.Items.Single().Accept(1,DateTimeOffset.UtcNow);f.User.UserId=f.VolunteerId;f.User.Role="VOLUNTEER";
        var act=()=>f.Attendance(new AttendanceTestStore(f)).CheckInAsync(row.Id,null,Guid.NewGuid());
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="TASK_INVALID_STATE");
    }
}
