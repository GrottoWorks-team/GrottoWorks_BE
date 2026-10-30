using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;

namespace TaskExecution.UnitTests.Application;

public sealed class TaskApplicationReviewTests
{
    [Fact]
    public async Task Approval_uses_the_same_row_and_reserves_capacity_without_another_acceptance()
    {
        var f = new AssignmentTestFixture();
        var row = TaskAssignment.Apply(f.Task.Id, f.VolunteerId, null, DateTimeOffset.UtcNow);
        f.Store.Add(row);
        var dto = await f.Service.ApproveApplicationAsync(row.Id, 1);
        dto.Status.Should().Be(AssignmentStatus.Accepted);
        row.ReservesCapacity.Should().BeTrue();
        f.Store.Items.Should().ContainSingle();
        f.Events.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Approval_cannot_claim_a_slot_already_reserved_by_a_direct_assignment()
    {
        var f = new AssignmentTestFixture();
        await f.Service.AssignAsync(f.Command());
        var row = TaskAssignment.Apply(f.Task.Id, Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        f.Identity.Items[row.VolunteerId] = new(row.VolunteerId,"Other","ACTIVE","VOLUNTEER",f.User.ParishId,null,[]);
        f.Store.Add(row);
        var act = () => f.Service.ApproveApplicationAsync(row.Id,1);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="TASK_CAPACITY_EXCEEDED");
        row.Status.Should().Be(AssignmentStatus.Pending);
    }

    [Fact]
    public async Task Rejection_keeps_history_and_does_not_reserve_a_slot()
    {
        var f = new AssignmentTestFixture();
        var row = TaskAssignment.Apply(f.Task.Id, f.VolunteerId,"original",DateTimeOffset.UtcNow);
        f.Store.Add(row);
        var dto = await f.Service.RejectApplicationAsync(row.Id," not suitable ",1);
        dto.Status.Should().Be("REJECTED");
        dto.Reason.Should().Be("not suitable");
        row.Note.Should().Be("original");
        row.ReservesCapacity.Should().BeFalse();
        var act = () => f.Service.ApproveApplicationAsync(row.Id,1);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="TASK_ASSIGNMENT_VERSION_MISMATCH");
    }
}
