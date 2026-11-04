using FluentAssertions;
using TaskExecution.Contracts.Events;

namespace TaskExecution.UnitTests.Application;

public sealed class NotificationEventContextTests
{
    [Fact]
    public async Task Direct_assignment_and_response_include_type_and_current_leader()
    {
        var fixture = new AssignmentTestFixture();
        var assignment = await fixture.Service.AssignAsync(fixture.Command());
        var created = fixture.Events.Items.Single();
        created.Payload.GetProperty("assignmentType").GetString().Should().Be("DIRECT");
        created.Payload.GetProperty("leaderUserId").GetGuid().Should().Be(fixture.Area.LeaderUserId!.Value);

        fixture.User.UserId = fixture.VolunteerId;
        fixture.User.Role = "VOLUNTEER";
        await fixture.Service.RespondAsync(assignment.Id, false, "Unavailable", 1);
        var responded = fixture.Events.Items.Last();
        responded.Payload.GetProperty("assignmentType").GetString().Should().Be("DIRECT");
        responded.Payload.GetProperty("leaderUserId").GetGuid().Should().Be(fixture.Area.LeaderUserId!.Value);
        responded.Payload.GetProperty("decision").GetString().Should().Be("DECLINED");
    }

    [Theory]
    [InlineData(true, "ACCEPTED")]
    [InlineData(false, "REJECTED")]
    public async Task Application_review_preserves_the_workflow_and_volunteer_recipient(bool approve, string decision)
    {
        var fixture = new AssignmentTestFixture();
        var leaderId = fixture.User.UserId;
        fixture.User.UserId = fixture.VolunteerId;
        fixture.User.Role = "VOLUNTEER";
        var application = await fixture.Service.ApplyAsync(fixture.Task.Id, null);
        fixture.Events.Items.Single().Payload.GetProperty("leaderUserId").GetGuid().Should().Be(leaderId);
        fixture.User.UserId = leaderId;
        fixture.User.Role = "LEADER";
        if (approve) await fixture.Service.ApproveApplicationAsync(application.Id, 1);
        else await fixture.Service.RejectApplicationAsync(application.Id, "No space", 1);

        var message = fixture.Events.Items.Last();
        message.EventType.Should().Be(TaskEventTypes.AssignmentResponded);
        message.Payload.GetProperty("assignmentType").GetString().Should().Be("APPLICATION");
        message.Payload.GetProperty("volunteerUserId").GetGuid().Should().Be(fixture.VolunteerId);
        message.Payload.GetProperty("decision").GetString().Should().Be(decision);
    }

    [Theory]
    [InlineData(true, "APPROVED")]
    [InlineData(false, "REJECTED")]
    public async Task Both_correction_decisions_emit_recipient_context_without_relying_on_hours_changes(bool approve, string decision)
    {
        var (fixture, assignmentId) = await CheckInRequestTests.ReadyVolunteerAsync();
        var store = new AttendanceTestStore(fixture);
        var start = DateTimeOffset.UtcNow.AddHours(-4);
        var session = AttendanceCheckOutTests.OpenSession(assignmentId, fixture.VolunteerId, start);
        session.Close(start.AddHours(1), 1);
        store.Add(session);
        var correction = await fixture.Attendance(store).RequestCorrectionAsync(session.Id, start, start.AddHours(2), "Fix time", 2);
        fixture.User.UserId = fixture.Area.LeaderUserId!.Value;
        fixture.User.Role = "LEADER";
        await fixture.Attendance(store).ReviewCorrectionAsync(correction.Id, approve, approve ? null : "Not supported", 1);

        var reviewed = fixture.Events.Items.Single(message => message.EventType == TaskEventTypes.AttendanceReviewed);
        reviewed.Payload.GetProperty("reviewType").GetString().Should().Be("CORRECTION");
        reviewed.Payload.GetProperty("decision").GetString().Should().Be(decision);
        reviewed.Payload.GetProperty("taskId").GetGuid().Should().Be(fixture.Task.Id);
        reviewed.Payload.GetProperty("volunteerUserId").GetGuid().Should().Be(fixture.VolunteerId);
    }
}
