using System.Text.Json;
using FluentAssertions;
using NotificationAudit.Application.Events;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class TaskWorkflowNotificationTests
{
    private static readonly Guid VolunteerId = Guid.NewGuid();
    private static readonly Guid LeaderId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    [Theory]
    [InlineData("task.assignment.created", "DIRECT", null, false, "New task assignment")]
    [InlineData("task.assignment.created", "APPLICATION", null, true, "Task application awaiting review")]
    [InlineData("task.assignment.responded", "APPLICATION", "ACCEPTED", false, "Task application approved")]
    [InlineData("task.assignment.responded", "APPLICATION", "REJECTED", false, "Task application rejected")]
    [InlineData("task.assignment.responded", "DIRECT", "DECLINED", true, "Task assignment declined")]
    [InlineData("attendance.request.submitted", null, null, true, "Check-in awaiting review")]
    public void Workflow_selects_the_correct_recipient(string type, string? assignmentType, string? decision, bool leader, string title)
    {
        var message = Message(type, new { taskId = TaskId, volunteerUserId = VolunteerId, leaderUserId = LeaderId, assignmentType, decision });
        var candidates = IntegrationEventIngestionHandler.MapNotifications(message, out var pending);
        pending.Should().BeFalse();
        var candidate = candidates.Should().ContainSingle().Which;
        candidate.RecipientUserId.Should().Be(leader ? LeaderId : VolunteerId);
        candidate.Title.Should().Be(title);
        candidate.ReferenceId.Should().Be(TaskId);
    }

    [Theory]
    [InlineData("APPROVED", "Attendance correction approved")]
    [InlineData("REJECTED", "Attendance correction rejected")]
    public void Corrections_are_not_presented_as_check_in_reviews(string decision, string title)
    {
        var message = Message("attendance.request.reviewed", new { taskId = TaskId, volunteerUserId = VolunteerId, reviewType = "CORRECTION", decision });
        var candidates = IntegrationEventIngestionHandler.MapNotifications(message, out var pending);
        pending.Should().BeFalse();
        candidates.Should().ContainSingle().Which.Title.Should().Be(title);
    }

    [Theory]
    [InlineData("task.assignment.responded", "DIRECT", "ACCEPTED")]
    [InlineData("attendance.request.reviewed", null, "CANCELLED")]
    public void Audit_only_decisions_do_not_get_stuck_in_context_backlog(string type, string? assignmentType, string decision)
    {
        var candidates = IntegrationEventIngestionHandler.MapNotifications(Message(type, new { assignmentType, decision }), out var pending);
        candidates.Should().BeEmpty();
        pending.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("UNKNOWN")]
    public void Ambiguous_legacy_assignment_never_guesses_that_it_is_a_direct_assignment(string? assignmentType)
    {
        var candidates = IntegrationEventIngestionHandler.MapNotifications(Message("task.assignment.created", new { volunteerUserId = VolunteerId, assignmentType }), out var pending);
        candidates.Should().BeEmpty();
        pending.Should().BeTrue();
    }

    [Fact]
    public async Task Pending_application_keeps_only_safe_workflow_context_for_reprocessing()
    {
        var store = new Store();
        await new IntegrationEventIngestionHandler(store).HandleAsync(Message("task.assignment.created", new
        {
            taskId = TaskId, volunteerUserId = VolunteerId, assignmentType = "APPLICATION",
            note = "Private note", password = "secret"
        }), default);
        store.Plan!.Status.Should().Be("REQUIRES_CONTEXT");
        store.Plan.SafeContext!.Payload.GetProperty("assignmentType").GetString().Should().Be("APPLICATION");
        store.Plan.SafeContext.Payload.GetRawText().Should().NotContain("Private").And.NotContain("secret");
    }

    [Fact]
    public async Task Correction_type_and_identifiers_survive_safe_context_redaction()
    {
        var store = new Store();
        var correctionId = Guid.NewGuid();
        await new IntegrationEventIngestionHandler(store).HandleAsync(Message("attendance.request.reviewed", new
        { correctionId, reviewType = "CORRECTION", decision = "REJECTED" }), default);
        store.Plan!.SafeContext!.Payload.GetProperty("reviewType").GetString().Should().Be("CORRECTION");
        store.Plan.SafeContext.Payload.GetProperty("correctionId").GetGuid().Should().Be(correctionId);
    }

    private static IncomingIntegrationEvent Message(string type, object payload) => new(Guid.NewGuid(), type,
        DateTimeOffset.UtcNow, Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "task", TaskId, JsonSerializer.SerializeToElement(payload));

    private sealed class Store : INotificationEventStore
    {
        public EventIngestionPlan? Plan { get; private set; }
        public Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken)
        { Plan = plan; return Task.CompletedTask; }
    }
}
