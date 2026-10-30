using FluentAssertions;
using TaskExecution.Application.Assignments;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.UnitTests.Application;

public sealed class DirectAssignmentTests
{
    [Fact]
    public async Task Two_volunteers_competing_for_last_slot_only_reserve_one()
    {
        var f = new AssignmentTestFixture();
        var second = Guid.NewGuid();
        f.Identity.Items[second] = f.Identity.Items[f.VolunteerId] with { UserId=second };
        var outcomes = await System.Threading.Tasks.Task.WhenAll(new[] {f.VolunteerId,second}.Select(async id =>
        {
            try { await f.Service.AssignAsync(f.Command() with {VolunteerId=id}); return "CREATED"; }
            catch (DomainRuleException error) { return error.Code; }
        }));
        outcomes.Should().BeEquivalentTo(new[] {"CREATED","TASK_CAPACITY_EXCEEDED"});
        f.Store.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Assigned_leader_creates_pending_assignment_with_capacity_and_event()
    {
        var f = new AssignmentTestFixture();
        f.Store.ScheduleConflict = true;
        var result = await f.Service.AssignAsync(f.Command());
        result.Status.Should().Be(AssignmentStatus.Pending);
        result.Warnings.Should().Contain("SCHEDULE_CONFLICT");
        f.Store.Items.Single().ReservesCapacity.Should().BeTrue();
        f.Task.Version.Should().Be(2);
        f.Events.Items.Single().EventType.Should().Be(TaskEventTypes.AssignmentCreated);
        f.Events.Items.Single().ActorId.Should().Be(f.User.UserId);
        f.Store.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_request_does_not_create_second_assignment()
    {
        var f = new AssignmentTestFixture();
        await f.Service.AssignAsync(f.Command());
        var act = () => f.Service.AssignAsync(f.Command());
        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_ASSIGNMENT_DUPLICATE");
        f.Store.Items.Should().HaveCount(1);
    }

    [Fact]
    public void Full_capacity_requires_explicit_override()
    {
        var row = TaskAssignment.Assign(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null,false,false,null,DateTimeOffset.UtcNow);
        var act = () => AssignmentService.EnsureCapacity([row],1,false);
        act.Should().Throw<DomainRuleException>().Which.Code.Should().Be("TASK_CAPACITY_EXCEEDED");
        AssignmentService.EnsureCapacity([row],1,true);
    }

    [Fact]
    public async Task Inactive_user_and_different_tenant_are_rejected()
    {
        var f = new AssignmentTestFixture();
        f.Identity.Items[f.VolunteerId] = f.Identity.Items[f.VolunteerId] with { Status="LOCKED" };
        var act = () => f.Service.AssignAsync(f.Command());
        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("USER_NOT_ACTIVE");
        f.Identity.Items[f.VolunteerId] = f.Identity.Items[f.VolunteerId] with { Status="ACTIVE",ParishId=Guid.NewGuid() };
        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("PARISH_ACCESS_DENIED");
    }

    [Fact]
    public async Task Missing_skill_level_dependency_is_not_silently_bypassed()
    {
        var f = new AssignmentTestFixture();
        f.Task.ReplaceSkills([new TaskSkillRequirement(Guid.NewGuid(),"ADVANCED",null)],null,DateTimeOffset.UtcNow);
        var act = () => f.Service.AssignAsync(f.Command() with { OverrideSkillMismatch=true,OverrideReason="Approved exception" });
        (await act.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SKILL_LEVEL_VALIDATION_UNAVAILABLE");
    }

    [Fact]
    public void Override_requires_reason_and_preserves_actor_audit()
    {
        var leader = Guid.NewGuid();
        var act = () => TaskAssignment.Assign(Guid.NewGuid(),Guid.NewGuid(),leader,null,true,false,null,DateTimeOffset.UtcNow);
        act.Should().Throw<DomainRuleException>();
        var row = TaskAssignment.Assign(Guid.NewGuid(),Guid.NewGuid(),leader,null,true,false,"  Required extra volunteer  ",DateTimeOffset.UtcNow);
        row.OverrideReason.Should().Be("Required extra volunteer");
        row.AssignedBy.Should().Be(leader);
    }
}
