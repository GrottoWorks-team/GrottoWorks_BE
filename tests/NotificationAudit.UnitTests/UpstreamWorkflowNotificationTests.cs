using System.Text.Json;
using FluentAssertions;
using NotificationAudit.Application.Events;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class UpstreamWorkflowNotificationTests
{
    [Theory]
    [InlineData("COMPLETED", true)]
    [InlineData("REVISION_REQUIRED", true)]
    [InlineData("IN_PROGRESS", false)]
    public void Review_results_await_their_recipient_integration_while_ordinary_state_changes_remain_audit_only(string status, bool expectedPending)
    {
        var candidates = IntegrationEventIngestionHandler.MapNotifications(Message("task.status.changed", new { status }), out var pending);
        candidates.Should().BeEmpty();
        pending.Should().Be(expectedPending);
    }

    [Theory]
    [InlineData("purchase.request.submitted", null, "leaderUserId", "purchaseRequestId", "PURCHASE_REQUEST")]
    [InlineData("purchase.request.decided", "APPROVED", "requestedByUserId", "purchaseRequestId", "PURCHASE_REQUEST")]
    [InlineData("purchase.request.decided", "REJECTED", "requestedByUserId", "purchaseRequestId", "PURCHASE_REQUEST")]
    [InlineData("readiness.item.failed", null, "leaderUserId", "workAreaId", "WORK_AREA")]
    [InlineData("recognition.entry.created", null, "awardedToUserId", "recognitionPointId", "RECOGNITION_POINT")]
    public void Source_fact_selects_the_recipient_instead_of_the_actor(string type, string? decision, string recipientField,
        string referenceField, string referenceType)
    {
        var recipient = Guid.NewGuid();
        var reference = Guid.NewGuid();
        var payload = new Dictionary<string, object?>
        {
            [recipientField] = recipient, [referenceField] = reference, ["decision"] = decision,
            ["mandatory"] = true, ["note"] = "Private note https://private.example/file"
        };
        var message = Message(type, payload);
        var candidates = IntegrationEventIngestionHandler.MapNotifications(message, out var pending);
        pending.Should().BeFalse();
        var candidate = candidates.Should().ContainSingle().Which;
        candidate.RecipientUserId.Should().Be(recipient).And.NotBe(message.ActorId);
        candidate.ReferenceId.Should().Be(reference);
        candidate.ReferenceType.Should().Be(referenceType);
        candidate.Body.Should().NotContain("Private").And.NotContain("https:");
    }

    [Fact]
    public void Optional_failed_item_remains_audit_only()
    {
        var candidates = IntegrationEventIngestionHandler.MapNotifications(Message("readiness.item.failed", new
        { mandatory = false, leaderUserId = Guid.NewGuid(), workAreaId = Guid.NewGuid() }), out var pending);
        pending.Should().BeFalse();
        candidates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("purchase.request.submitted")]
    [InlineData("purchase.request.decided")]
    [InlineData("readiness.item.failed")]
    [InlineData("recognition.entry.created")]
    public void Missing_source_context_is_preserved_instead_of_guessing_from_actor_or_aggregate(string type)
    {
        var candidates = IntegrationEventIngestionHandler.MapNotifications(Message(type, new { }), out var pending);
        pending.Should().BeTrue();
        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Mandatory_flag_is_retained_so_replay_keeps_the_same_business_decision()
    {
        var store = new Store();
        await new IntegrationEventIngestionHandler(store).HandleAsync(Message("readiness.item.failed", new
        { mandatory = true, workAreaId = Guid.NewGuid() }), default);
        store.Plan!.Status.Should().Be("REQUIRES_CONTEXT");
        store.Plan.SafeContext!.Payload.GetProperty("mandatory").GetBoolean().Should().BeTrue();
    }

    private static IncomingIntegrationEvent Message(string type, object payload) => new(Guid.NewGuid(), type,
        DateTimeOffset.UtcNow, Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "resource", Guid.NewGuid(), JsonSerializer.SerializeToElement(payload));

    private sealed class Store : INotificationEventStore
    {
        public EventIngestionPlan? Plan { get; private set; }
        public Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken)
        { Plan = plan; return Task.CompletedTask; }
    }
}
