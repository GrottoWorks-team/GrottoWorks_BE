using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.UnitTests.Application;

public sealed class AttendanceCorrectionRequestTests
{
    [Fact]
    public async Task Request_keeps_old_and_new_times_without_changing_approved_hours()
    {
        var (f,id)=await CheckInRequestTests.ReadyVolunteerAsync();var store=new AttendanceTestStore(f);
        var start=DateTimeOffset.UtcNow.AddHours(-3);
        var session=AttendanceCheckOutTests.OpenSession(id,f.VolunteerId,start);session.Close(start.AddHours(1),1);store.Add(session);
        var dto=await f.Attendance(store).RequestCorrectionAsync(session.Id,start,start.AddHours(2)," missed time ",2);
        dto.OriginalCheckOutAt.Should().Be(start.AddHours(1));dto.RequestedCheckOutAt.Should().Be(start.AddHours(2));
        dto.Reason.Should().Be("missed time");session.ApprovedMinutes.Should().Be(60);
        dto.Status.Should().Be(AttendanceCorrectionStatus.Pending);
        var act=()=>f.Attendance(store).RequestCorrectionAsync(session.Id,start,start.AddHours(2),"again",2);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="ATTENDANCE_CORRECTION_PENDING");
    }

    [Fact]
    public void Request_cannot_claim_future_time_or_omit_reason()
    {
        var now=DateTimeOffset.UtcNow;
        var session=AttendanceCheckOutTests.OpenSession(Guid.NewGuid(),Guid.NewGuid(),now.AddHours(-1));
        var act=()=>AttendanceCorrection.Request(session,Guid.NewGuid(),now.AddHours(-1),now.AddHours(1),"fix",now);
        act.Should().Throw<DomainRuleException>();
        act=()=>AttendanceCorrection.Request(session,Guid.NewGuid(),now.AddHours(-1),now," ",now);
        act.Should().Throw<DomainRuleException>();
    }
}
