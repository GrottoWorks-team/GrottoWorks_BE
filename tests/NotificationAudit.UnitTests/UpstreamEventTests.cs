using System.Text.Json;
using FluentAssertions;
using Identity.Contracts.Events;
using NotificationAudit.Application.Events;
using NotificationAudit.Infrastructure.Events;
using ParishCoordination.Contracts.Events;
using Readiness.Contracts.Events;
using Reporting.Contracts.Events;
using Resource.Contracts.Events;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class UpstreamEventTests
{
    [Fact]
    public void Adapters_preserve_source_identity_time_actor_tenant_and_schema()
    {
        var id = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var parish = Guid.NewGuid();
        var season = Guid.NewGuid();
        var aggregate = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.SerializeToElement(new { workAreaId = aggregate });
        IncomingIntegrationEvent[] normalized =
        [
            UpstreamEventAdapter.Normalize(new IdentityIntegrationEvent(id, "identity.user.locked", 1, now, "correlation", actor, parish, season, "user", aggregate, 4, payload)),
            UpstreamEventAdapter.Normalize(new PlanningIntegrationEvent(id, "planning.season.activated", 1, now, "correlation", actor, parish, season, "season", aggregate, 4, payload)),
            UpstreamEventAdapter.Normalize(new ResourceIntegrationEvent(id, "purchase.request.submitted", 1, now, "correlation", actor, parish, season, "purchase_request", aggregate, payload)),
            UpstreamEventAdapter.Normalize(new ReadinessIntegrationEvent(id, "readiness.area.ready", 1, now, "correlation", actor, parish, season, "work_area", aggregate, payload)),
            UpstreamEventAdapter.Normalize(new ReportingIntegrationEvent(id, "recognition.entry.created", 1, now, "correlation", actor, parish, season, "recognition", aggregate, payload))
        ];
        foreach (var message in normalized)
        {
            message.EventId.Should().Be(id);
            message.ActorId.Should().Be(actor);
            message.ParishId.Should().Be(parish);
            message.SeasonId.Should().Be(season);
            message.OccurredAt.Should().Be(now);
            message.SchemaVersion.Should().Be(1);
            message.CorrelationId.Should().Be("correlation");
            message.Payload.GetProperty("workAreaId").GetGuid().Should().Be(aggregate);
        }
        normalized[0].AggregateVersion.Should().Be(4);
        normalized[2].AggregateVersion.Should().BeNull("unversioned source metadata must not be fabricated");
    }

    [Fact]
    public void Missing_optional_identity_scope_is_not_replaced_with_another_tenant()
    {
        var message = new IdentityIntegrationEvent(Guid.NewGuid(), "identity.user.registered", 1,
            DateTimeOffset.UtcNow, "correlation", null, null, null, "user", Guid.NewGuid(), 1, JsonSerializer.SerializeToElement(new { }));
        var normalized = UpstreamEventAdapter.Normalize(message);
        normalized.ActorId.Should().Be(Guid.Empty);
        normalized.ParishId.Should().Be(Guid.Empty);
        normalized.SeasonId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task Known_notification_waits_for_context_while_audit_only_event_completes()
    {
        var store = new Store();
        var handler = new IntegrationEventIngestionHandler(store);
        var requester = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToElement(new { requestedByUserId = requester, password = "secret" });
        var source = new ResourceIntegrationEvent(Guid.NewGuid(), "purchase.request.submitted", 1,
            DateTimeOffset.UtcNow, Guid.NewGuid().ToString(), null, Guid.NewGuid(), Guid.NewGuid(), "purchase_request", Guid.NewGuid(), payload);
        await handler.HandleAsync(UpstreamEventAdapter.Normalize(source), default);
        store.Plan!.Status.Should().Be("REQUIRES_CONTEXT");
        store.Plan.SafeContext!.Payload.GetProperty("requestedByUserId").GetGuid().Should().Be(requester);
        store.Plan.SafeContext.Payload.GetRawText().Should().NotContain("secret");
        await handler.HandleAsync(UpstreamEventAdapter.Normalize(source with { EventId = Guid.NewGuid(), EventType = "material.shortage.changed" }), default);
        store.Plan!.Status.Should().Be("PROCESSED");
    }
    private sealed class Store : INotificationEventStore
    {
        public EventIngestionPlan? Plan { get; private set; }
        public Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken) { Plan = plan; return Task.CompletedTask; }
    }
}
