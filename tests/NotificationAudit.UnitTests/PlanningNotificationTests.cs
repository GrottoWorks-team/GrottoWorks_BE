using System.Text.Json;
using FluentAssertions;
using NotificationAudit.Application.Events;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class PlanningNotificationTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Only_the_new_holder_of_the_changed_role_is_notified(bool leaderChanged, bool materialOfficerChanged)
    {
        var leader = Guid.NewGuid();
        var officer = Guid.NewGuid();
        var area = Guid.NewGuid();
        var notifications = IntegrationEventIngestionHandler.MapNotifications(Message(new
        {
            workAreaId = area, leaderUserId = leader, materialOfficerUserId = officer, leaderChanged, materialOfficerChanged
        }), out var pending);
        pending.Should().BeFalse();
        var notification = notifications.Should().ContainSingle().Which;
        notification.RecipientUserId.Should().Be(leaderChanged ? leader : officer);
        notification.ReferenceId.Should().Be(area);
        notification.ReferenceType.Should().Be("WORK_AREA");
    }

    [Fact]
    public void Community_change_is_audited_without_notifying_unchanged_role_holders()
    {
        var notifications = IntegrationEventIngestionHandler.MapNotifications(Message(new
        {
            workAreaId = Guid.NewGuid(), leaderUserId = Guid.NewGuid(), materialOfficerUserId = Guid.NewGuid(),
            communityChanged = true, leaderChanged = false, materialOfficerChanged = false
        }), out var pending);
        notifications.Should().BeEmpty();
        pending.Should().BeFalse();
    }

    [Fact]
    public void One_user_assigned_both_responsibilities_gets_one_notification()
    {
        var recipient = Guid.NewGuid();
        var notifications = IntegrationEventIngestionHandler.MapNotifications(Message(new
        {
            workAreaId = Guid.NewGuid(), leaderUserId = recipient, materialOfficerUserId = recipient,
            leaderChanged = true, materialOfficerChanged = true
        }), out var pending);
        pending.Should().BeFalse();
        notifications.Should().ContainSingle().Which.RecipientUserId.Should().Be(recipient);
    }

    [Fact]
    public void Legacy_snapshot_without_change_flags_is_not_mistaken_for_new_role_assignments()
    {
        var notifications = IntegrationEventIngestionHandler.MapNotifications(Message(new
        { workAreaId = Guid.NewGuid(), leaderUserId = Guid.NewGuid(), materialOfficerUserId = Guid.NewGuid() }), out var pending);
        pending.Should().BeTrue();
        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Retained_context_preserves_change_flags_without_copying_the_full_snapshot()
    {
        var store = new Store();
        await new IntegrationEventIngestionHandler(store).HandleAsync(Message(new
        {
            workAreaId = Guid.NewGuid(), leaderUserId = Guid.NewGuid(), leaderChanged = true,
            name = "Private area name", description = "Private description"
        }), default);
        store.Plan!.Status.Should().Be("REQUIRES_CONTEXT");
        store.Plan.SafeContext!.Payload.GetProperty("leaderChanged").GetBoolean().Should().BeTrue();
        store.Plan.SafeContext.Payload.GetRawText().Should().NotContain("Private");
    }

    private static IncomingIntegrationEvent Message(object payload) => new(Guid.NewGuid(), "planning.work-area.assigned",
        DateTimeOffset.UtcNow, Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "work_area",
        Guid.NewGuid(), JsonSerializer.SerializeToElement(payload));

    private sealed class Store : INotificationEventStore
    {
        public EventIngestionPlan? Plan { get; private set; }
        public Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken)
        { Plan = plan; return Task.CompletedTask; }
    }
}
