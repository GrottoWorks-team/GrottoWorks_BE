using System.Text.Json;
using FluentAssertions;
using NotificationAudit.Application.Events;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class TaskEventIngestionTests
{
    [Fact]
    public void Direct_assignment_maps_to_the_volunteer()
    {
        var volunteerId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var payload = JsonDocument.Parse($$"""{"volunteerUserId":"{{volunteerId}}","taskId":"{{taskId}}"}""");
        var message = Create("task.assignment.created", payload.RootElement);

        var notifications = TaskEventIngestionHandler.MapNotifications(message, out var requiresContext);

        requiresContext.Should().BeFalse();
        notifications.Should().ContainSingle().Which.RecipientUserId.Should().Be(volunteerId);
        notifications[0].ReferenceId.Should().Be(taskId);
    }

    [Fact]
    public void Missing_recipient_is_marked_as_requiring_context()
    {
        using var payload = JsonDocument.Parse("{}");
        var notifications = TaskEventIngestionHandler.MapNotifications(
            Create("attendance.session.open-too-long", payload.RootElement),
            out var requiresContext);
        requiresContext.Should().BeTrue();
        notifications.Should().BeEmpty();
    }

    [Fact]
    public void Long_open_session_notifies_volunteer_and_leader_once_each()
    {
        var volunteerId = Guid.NewGuid();
        var leaderId = Guid.NewGuid();
        using var payload = JsonDocument.Parse($$"""{"volunteerUserId":"{{volunteerId}}","leaderUserId":"{{leaderId}}"}""");
        var notifications = TaskEventIngestionHandler.MapNotifications(
            Create("attendance.session.open-too-long", payload.RootElement),
            out var requiresContext);
        requiresContext.Should().BeFalse();
        notifications.Select(item => item.RecipientUserId).Should().BeEquivalentTo(new[] { volunteerId, leaderId });
    }

    private static IncomingTaskEvent Create(string eventType, JsonElement payload) => new(
        Guid.NewGuid(), eventType, DateTimeOffset.UtcNow, Guid.NewGuid().ToString(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "TASK", Guid.NewGuid(), payload.Clone());
}
