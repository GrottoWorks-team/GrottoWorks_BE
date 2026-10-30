using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;

namespace TaskExecution.UnitTests.Application;

public sealed class TaskApplicationTests
{
    [Fact]
    public async Task Pending_application_does_not_reserve_a_slot_and_is_visible_to_the_leader()
    {
        var f = new AssignmentTestFixture();
        var leader = f.User.UserId;
        f.User.UserId = f.VolunteerId;
        f.User.Role = "VOLUNTEER";
        var dto = await f.Service.ApplyAsync(f.Task.Id, " help ");
        dto.Status.Should().Be("PENDING");
        dto.Note.Should().Be("help");
        f.Store.Items.Single().ReservesCapacity.Should().BeFalse();
        f.Events.Items.Should().ContainSingle();
        f.User.UserId = leader;
        f.User.Role = "LEADER";
        var page = await f.Service.ApplicationsAsync(f.Task.Id, 1, 20, false);
        page.Total.Should().Be(1);
        page.Items.Single().Id.Should().Be(dto.Id);
    }

    [Fact]
    public async Task Repeated_application_is_rejected_without_creating_another_row()
    {
        var f = new AssignmentTestFixture();
        f.User.UserId = f.VolunteerId;
        f.User.Role = "VOLUNTEER";
        await f.Service.ApplyAsync(f.Task.Id, null);
        var act = () => f.Service.ApplyAsync(f.Task.Id, null);
        await act.Should().ThrowAsync<DomainRuleException>().Where(e => e.Code == "TASK_ASSIGNMENT_DUPLICATE");
        f.Store.Items.Should().ContainSingle();
    }

    [Fact]
    public void Application_factory_rejects_empty_identity_and_oversized_note()
    {
        var act = () => TaskAssignment.Apply(Guid.Empty, Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        act.Should().Throw<DomainRuleException>();
        act = () => TaskAssignment.Apply(Guid.NewGuid(), Guid.NewGuid(), new string('a',2001), DateTimeOffset.UtcNow);
        act.Should().Throw<DomainRuleException>();
    }
}
