using FluentAssertions;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.UnitTests.Application;

public sealed class MyTasksTests
{
    [Fact]
    public async Task Handler_passes_only_the_current_volunteer_and_parish_to_the_query()
    {
        var f = new AssignmentTestFixture();
        f.User.Role = "VOLUNTEER";
        f.User.UserId = f.VolunteerId;
        var store = new Store(f.Task);
        var filter = new MyTasksFilter(null,TaskType.Work,f.Area.SeasonId,1,20,null);
        var result = await new MyTasksQueryHandler(store,f.User).HandleAsync(filter);
        store.Volunteer.Should().Be(f.VolunteerId);
        store.Parish.Should().Be(f.User.ParishId!.Value);
        store.Filter.Should().Be(filter);
        result.Items.Single().Id.Should().Be(f.Task.Id);
    }

    [Fact]
    public async Task Caller_without_a_parish_cannot_query_personal_tasks()
    {
        var f = new AssignmentTestFixture();
        f.User.Role = "VOLUNTEER";
        f.User.ParishId = null;
        var store = new Store(f.Task);
        var act = () => new MyTasksQueryHandler(store,f.User).HandleAsync(new(null,null,null,1,20,null));
        await act.Should().ThrowAsync<DomainRuleException>();
        store.Filter.Should().BeNull();
    }

    private sealed class Store(WorkTask task) : IMyTasksStore
    {
        public Guid Volunteer; public Guid Parish; public MyTasksFilter? Filter;
        public Task<(IReadOnlyList<WorkTask> Items,long Total)> GetAsync(Guid id,Guid parish,MyTasksFilter filter,CancellationToken ct)
        {
            Volunteer=id; Parish=parish; Filter=filter;
            return Task.FromResult<(IReadOnlyList<WorkTask>,long)>(([task],1));
        }
    }
}
