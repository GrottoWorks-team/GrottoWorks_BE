using FluentAssertions;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class TaskRecruitmentTests
{
    [Theory]
    [InlineData(TaskType.Work, TaskState.NotStarted)]
    [InlineData(TaskType.Work, TaskState.InProgress)]
    [InlineData(TaskType.Corrective, TaskState.NotStarted)]
    [InlineData(TaskType.Corrective, TaskState.InProgress)]
    public async Task Leader_can_invite_and_volunteer_can_accept_without_changing_task_state(
        TaskType type, TaskState state)
    {
        var fixture = CreateFixture(type, state);

        var invitation = await fixture.Service.AssignAsync(fixture.Command());
        invitation.Status.Should().Be(AssignmentStatus.Pending);
        UseVolunteer(fixture);
        var accepted = await fixture.Service.RespondAsync(invitation.Id, true, null, invitation.Version);

        accepted.Status.Should().Be(AssignmentStatus.Accepted);
        fixture.Task.Status.Should().Be(state);
        fixture.Events.Items.Select(message => message.EventType).Should()
            .Equal(TaskEventTypes.AssignmentCreated, TaskEventTypes.AssignmentResponded);
    }

    [Theory]
    [InlineData(TaskState.NotStarted, true)]
    [InlineData(TaskState.NotStarted, false)]
    [InlineData(TaskState.InProgress, true)]
    [InlineData(TaskState.InProgress, false)]
    public async Task Volunteer_can_apply_and_leader_can_review_without_changing_task_state(
        TaskState state, bool approve)
    {
        var fixture = CreateFixture(TaskType.Work, state);
        var leaderId = fixture.User.UserId;
        UseVolunteer(fixture);

        var application = await fixture.Service.ApplyAsync(fixture.Task.Id, null);

        application.Status.Should().Be("PENDING");
        var assignment = fixture.Store.Items.Should().ContainSingle().Which;
        assignment.ReservesCapacity.Should().BeFalse();
        fixture.User.UserId = leaderId;
        fixture.User.Role = "LEADER";
        if (approve)
            await fixture.Service.ApproveApplicationAsync(application.Id, application.Version);
        else
            await fixture.Service.RejectApplicationAsync(application.Id, "Unavailable dates", application.Version);

        assignment.Status.Should().Be(approve ? AssignmentStatus.Accepted : AssignmentStatus.Declined);
        assignment.ReservesCapacity.Should().Be(approve);
        fixture.Task.Status.Should().Be(state);
    }

    [Theory]
    [InlineData(TaskType.Purchase, TaskState.NotStarted)]
    [InlineData(TaskType.Purchase, TaskState.InProgress)]
    [InlineData(TaskType.Work, TaskState.Cancelled)]
    public async Task Recruitment_still_rejects_closed_states_for_the_task_type(TaskType type, TaskState state)
    {
        var fixture = CreateFixture(type, state);

        var invite = () => fixture.Service.AssignAsync(fixture.Command());
        (await invite.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_INVALID_STATE");
        UseVolunteer(fixture);
        var apply = () => fixture.Service.ApplyAsync(fixture.Task.Id, null);
        (await apply.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_INVALID_STATE");

        fixture.Store.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task An_active_task_still_enforces_capacity_when_adding_a_volunteer()
    {
        var fixture = CreateFixture(TaskType.Work, TaskState.InProgress);
        var existing = TaskAssignment.Assign(fixture.Task.Id, Guid.NewGuid(), fixture.User.UserId,
            null, false, false, null, DateTimeOffset.UtcNow);
        fixture.Store.Add(existing);

        var invite = () => fixture.Service.AssignAsync(fixture.Command());
        (await invite.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_CAPACITY_EXCEEDED");
        fixture.Store.Items.Should().ContainSingle();
    }

    private static void UseVolunteer(AssignmentTestFixture fixture)
    {
        fixture.User.UserId = fixture.VolunteerId;
        fixture.User.Role = "VOLUNTEER";
    }

    private static AssignmentTestFixture CreateFixture(TaskType type, TaskState state)
    {
        var fixture = new AssignmentTestFixture();
        var now = DateTimeOffset.UtcNow;
        if (type != TaskType.Work)
            fixture.Task.SetSpecialType(type, type == TaskType.Purchase ? Guid.NewGuid() : null);
        fixture.Task.MarkReady(null, now);
        if (state == TaskState.InProgress)
            fixture.Task.BeginWork(now);
        if (state == TaskState.Cancelled)
            fixture.Task.Cancel("No longer required", fixture.User.UserId, null, now);
        return fixture;
    }
}
