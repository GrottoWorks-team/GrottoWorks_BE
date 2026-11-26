using FluentAssertions;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.UnitTests.Application;

public sealed class AssignmentResponseTests
{
    [Fact]
    public async Task Owner_accepts_pending_assignment_and_does_not_reserve_another_slot()
    {
        var f = new AssignmentTestFixture();
        var assigned = await f.Service.AssignAsync(f.Command());
        f.User.UserId=f.VolunteerId;
        f.User.Role="VOLUNTEER";
        var result = await f.Service.RespondAsync(assigned.Id,true,null,1);
        result.Status.Should().Be(AssignmentStatus.Accepted);
        result.Version.Should().Be(2);
        f.Store.Items.Count(row=>row.ReservesCapacity).Should().Be(1);
        f.Events.Items.Last().EventType.Should().Be(TaskEventTypes.AssignmentResponded);
    }

    [Fact]
    public async Task Decline_releases_capacity_and_keeps_original_note_and_reason()
    {
        var f = new AssignmentTestFixture();
        var assigned = await f.Service.AssignAsync(f.Command() with {Note="Original note"});
        f.User.UserId=f.VolunteerId;
        f.User.Role="VOLUNTEER";
        var result=await f.Service.RespondAsync(assigned.Id,false,"  Not available  ",1);
        result.Status.Should().Be(AssignmentStatus.Declined);
        result.Note.Should().Be("Original note");
        result.ResponseReason.Should().Be("Not available");
        f.Store.Items.Single().ReservesCapacity.Should().BeFalse();
    }

    [Fact]
    public async Task Purchase_task_is_assigned_to_a_parish_buyer_who_can_accept_it()
    {
        // 26/11/2026: PURCHASE tasks go to a parish user (the buyer), not a volunteer.
        var f = new AssignmentTestFixture();
        var buyer = Guid.NewGuid();
        f.Identity.Items[buyer] = new(buyer, "Ban hành giáo", "ACTIVE", "PARISH", f.User.ParishId, null, []);
        f.Task.SetSpecialType(TaskType.Purchase, Guid.NewGuid());

        var assigned = await f.Service.AssignAsync(f.Command() with { VolunteerId = buyer });
        f.User.UserId = buyer;
        f.User.Role = "PARISH";
        var accepted = await f.Service.RespondAsync(assigned.Id, true, null, assigned.Version);

        accepted.Status.Should().Be(AssignmentStatus.Accepted);
        accepted.VolunteerId.Should().Be(buyer);
    }

    [Fact]
    public async Task Another_volunteer_is_denied_and_stale_version_is_rejected()
    {
        var f=new AssignmentTestFixture();
        var assigned=await f.Service.AssignAsync(f.Command());
        f.User.Role="VOLUNTEER";
        var wrong=()=>f.Service.RespondAsync(assigned.Id,true,null,1);
        (await wrong.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_ASSIGNMENT_ACCESS_DENIED");
        f.User.UserId=f.VolunteerId;
        var stale=()=>f.Service.RespondAsync(assigned.Id,true,null,9);
        (await stale.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }
}
