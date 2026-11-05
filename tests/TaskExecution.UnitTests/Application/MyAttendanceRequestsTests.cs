using FluentAssertions;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.UnitTests.Application;

public sealed class MyAttendanceRequestsTests
{
    [Fact]
    public async Task Lists_only_the_callers_pending_requests()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();
        var store=new AttendanceTestStore(f);
        var mine=AttendanceRequest.CheckIn(id,f.VolunteerId,"ready",DateTimeOffset.UtcNow);
        store.Add(mine);
        // Đơn của TNV khác không được lọt vào danh sách của tôi.
        store.Add(AttendanceRequest.CheckIn(id,Guid.NewGuid(),null,DateTimeOffset.UtcNow));
        f.User.UserId=f.VolunteerId;f.User.Role="VOLUNTEER";

        var (items,total)=await f.Attendance(store).MyRequestsAsync(AttendanceRequestStatus.Pending,1,20,true);

        total.Should().Be(1);
        items.Single().Id.Should().Be(mine.Id);
        items.Single().AssignmentId.Should().Be(id);
        items.Single().Status.Should().Be(AttendanceRequestStatus.Pending);
    }
}
