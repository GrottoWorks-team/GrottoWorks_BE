using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskState=TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class CancelTaskTests
{
    [Fact]
    public async Task Cancellation_retains_assignment_history_and_records_audited_event()
    {
        var f=new AssignmentTestFixture();
        var dto=await f.Service.AssignAsync(f.Command());
        var result=await f.Service.CancelTaskAsync(f.Task.Id," weather ",f.Task.Version);
        result.Status.Should().Be(TaskState.Cancelled);
        f.Task.CancellationReason.Should().Be("weather");
        f.Task.CancelledBy.Should().Be(f.User.UserId);
        f.Store.Items.Single().Id.Should().Be(dto.Id);
        f.Store.Items.Single().Status.Should().Be(AssignmentStatus.Cancelled);
        f.Events.Items.Last().EventType.Should().Be("task.status.changed");
    }

    [Fact]
    public async Task Stale_version_does_not_cancel_the_task_or_assignment()
    {
        var f=new AssignmentTestFixture();
        await f.Service.AssignAsync(f.Command());
        var act=()=>f.Service.CancelTaskAsync(f.Task.Id,"weather",1);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="TASK_VERSION_MISMATCH");
        f.Task.Status.Should().Be(TaskState.Open);
        f.Store.Items.Single().Status.Should().Be(AssignmentStatus.Pending);
    }

    [Fact]
    public void Cancellation_requires_a_reason_and_is_not_repeatable()
    {
        var f=new AssignmentTestFixture();
        var act=()=>f.Task.Cancel(" ",f.User.UserId,1,DateTimeOffset.UtcNow);
        act.Should().Throw<DomainRuleException>();
        f.Task.Cancel("weather",f.User.UserId,1,DateTimeOffset.UtcNow);
        act=()=>f.Task.Cancel("again",f.User.UserId,2,DateTimeOffset.UtcNow);
        act.Should().Throw<DomainRuleException>().Which.Code.Should().Be("TASK_INVALID_STATE");
    }
}
