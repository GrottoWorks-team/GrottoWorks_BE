using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.UnitTests.Application;

public sealed class AttendanceCorrectionReviewTests
{
    [Fact]
    public async Task Approval_replaces_hours_and_keeps_the_old_time_in_correction_history()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var start=DateTimeOffset.UtcNow.AddHours(-4);var session=AttendanceCheckOutTests.OpenSession(id,f.VolunteerId,start);
        session.Close(start.AddHours(1),1);store.Add(session);
        var correction=await f.Attendance(store).RequestCorrectionAsync(session.Id,start,start.AddHours(2),"missing hour",2);
        f.User.UserId=f.Area.LeaderUserId!.Value;f.User.Role="LEADER";
        var dto=await f.Attendance(store).ReviewCorrectionAsync(correction.Id,true,null,1);
        dto.Status.Should().Be(AttendanceCorrectionStatus.Approved);session.ApprovedMinutes.Should().Be(120);
        session.Status.Should().Be(AttendanceStatus.Corrected);dto.OriginalCheckOutAt.Should().Be(start.AddHours(1));
        f.Events.Items.Last().Payload.GetProperty("previousApprovedMinutes").GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task Changed_session_prevents_approval_of_an_old_snapshot()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var start=DateTimeOffset.UtcNow.AddHours(-4);var session=AttendanceCheckOutTests.OpenSession(id,f.VolunteerId,start);store.Add(session);
        var dto=await f.Attendance(store).RequestCorrectionAsync(session.Id,start,start.AddHours(2),"fix",1);
        session.Close(start.AddHours(1),1);
        f.User.UserId=f.Area.LeaderUserId!.Value;f.User.Role="LEADER";
        var act=()=>f.Attendance(store).ReviewCorrectionAsync(dto.Id,true,null,1);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="ATTENDANCE_VERSION_MISMATCH");
        session.ApprovedMinutes.Should().Be(60);store.Corrections.Single().Status.Should().Be(AttendanceCorrectionStatus.Pending);
    }

    [Fact]
    public async Task Rejection_preserves_session_hours_and_requires_a_reason()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var start=DateTimeOffset.UtcNow.AddHours(-3);var session=AttendanceCheckOutTests.OpenSession(id,f.VolunteerId,start);session.Close(start.AddHours(1),1);store.Add(session);
        var correction=await f.Attendance(store).RequestCorrectionAsync(session.Id,start,start.AddHours(2),"fix",2);
        f.User.UserId=f.Area.LeaderUserId!.Value;f.User.Role="LEADER";
        var service=f.Attendance(store);var act=()=>service.ReviewCorrectionAsync(correction.Id,false,null,1);
        await act.Should().ThrowAsync<DomainRuleException>();
        await service.ReviewCorrectionAsync(correction.Id,false,"not supported",1);
        session.ApprovedMinutes.Should().Be(60);store.Corrections.Single().Status.Should().Be(AttendanceCorrectionStatus.Rejected);
    }
}
