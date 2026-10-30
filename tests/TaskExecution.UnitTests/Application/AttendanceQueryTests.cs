using FluentAssertions;
using TaskExecution.Application.Attendance;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.UnitTests.Application;

public sealed class AttendanceQueryTests
{
    [Fact]
    public async Task Personal_query_forwards_actor_parish_and_filters_without_reading_another_user()
    {
        var f=new AssignmentTestFixture();f.User.Role="VOLUNTEER";
        var store=new ReadStore();
        var queries=new AttendanceQueries(store,null!,f.User);
        var result=await queries.PersonalAsync(new(null,null,null,null,null,3,20,true));
        result.Total.Should().Be(0);result.Items.Should().BeEmpty();
        store.Actor.Should().Be(f.User.UserId);store.Parish.Should().Be(f.User.ParishId!.Value);store.Page.Should().Be(3);
    }

    [Fact]
    public async Task Working_hours_does_not_implicitly_allow_admin()
    {
        var f=new AssignmentTestFixture();f.User.Role="ADMIN";
        var queries=new AttendanceQueries(new ReadStore(),null!,f.User);
        var act=()=>queries.HoursAsync(f.Area.WorkAreaId,null,null);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Kind==DomainErrorKind.Forbidden);
    }

    [Fact]
    public void Inverted_range_is_a_bad_request()
    {
        var now=DateTimeOffset.UtcNow;
        var act=()=>AttendanceQueries.ValidateRange(now,now);
        act.Should().Throw<DomainRuleException>().Which.Kind.Should().Be(DomainErrorKind.BadRequest);
    }

    private sealed class ReadStore : IAttendanceReadStore
    {
        public Guid Actor,Parish;public int Page;
        public Task<(IReadOnlyList<AttendanceSession> Items,long Total)> PersonalAsync(Guid actor,Guid parish,AttendanceFilter filter,CancellationToken ct)
        {Actor=actor;Parish=parish;Page=filter.Page;return Task.FromResult<(IReadOnlyList<AttendanceSession>,long)>(([],0));}
        public Task<IReadOnlyList<WorkingHourSummary>> HoursAsync(Guid id,DateTimeOffset? from,DateTimeOffset? to,CancellationToken ct)=>Task.FromResult<IReadOnlyList<WorkingHourSummary>>([]);
    }
}
