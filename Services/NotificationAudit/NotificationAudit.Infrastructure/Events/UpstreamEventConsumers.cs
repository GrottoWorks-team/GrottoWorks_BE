using Identity.Contracts.Events;
using MassTransit;
using NotificationAudit.Application.Events;
using ParishCoordination.Contracts.Events;
using Readiness.Contracts.Events;
using Reporting.Contracts.Events;
using Resource.Contracts.Events;

namespace NotificationAudit.Infrastructure.Events;

public static class UpstreamEventAdapter
{
    public static IncomingIntegrationEvent Normalize(IdentityIntegrationEvent message) => new(message.EventId, message.EventType,
        message.OccurredAt, message.CorrelationId, message.ActorId ?? Guid.Empty, message.ParishId ?? Guid.Empty,
        message.SeasonId ?? Guid.Empty, message.AggregateType, message.AggregateId, message.Payload, message.Version, message.AggregateVersion);
    public static IncomingIntegrationEvent Normalize(PlanningIntegrationEvent message) => new(message.EventId, message.EventType,
        message.OccurredAt, message.CorrelationId, message.ActorId ?? Guid.Empty, message.ParishId,
        message.SeasonId, message.AggregateType, message.AggregateId, message.Payload, message.Version, message.AggregateVersion);
    public static IncomingIntegrationEvent Normalize(ResourceIntegrationEvent message) => new(message.EventId, message.EventType,
        message.OccurredAt, message.CorrelationId, message.ActorId ?? Guid.Empty, message.ParishId ?? Guid.Empty,
        message.SeasonId ?? Guid.Empty, message.AggregateType, message.AggregateId, message.Payload, message.Version);
    public static IncomingIntegrationEvent Normalize(ReadinessIntegrationEvent message) => new(message.EventId, message.EventType,
        message.OccurredAt, message.CorrelationId, message.ActorId ?? Guid.Empty, message.ParishId ?? Guid.Empty,
        message.SeasonId ?? Guid.Empty, message.AggregateType, message.AggregateId, message.Payload, message.Version);
    public static IncomingIntegrationEvent Normalize(ReportingIntegrationEvent message) => new(message.EventId, message.EventType,
        message.OccurredAt, message.CorrelationId, message.ActorId ?? Guid.Empty, message.ParishId ?? Guid.Empty,
        message.SeasonId ?? Guid.Empty, message.AggregateType, message.AggregateId, message.Payload, message.Version);
}

public sealed class IdentityEventConsumer(IntegrationEventIngestionHandler handler) : IConsumer<IdentityIntegrationEvent>
{
    public Task Consume(ConsumeContext<IdentityIntegrationEvent> context) => handler.HandleAsync(UpstreamEventAdapter.Normalize(context.Message), context.CancellationToken);
}
public sealed class PlanningEventConsumer(IntegrationEventIngestionHandler handler) : IConsumer<PlanningIntegrationEvent>
{
    public Task Consume(ConsumeContext<PlanningIntegrationEvent> context) => handler.HandleAsync(UpstreamEventAdapter.Normalize(context.Message), context.CancellationToken);
}
public sealed class ResourceEventConsumer(IntegrationEventIngestionHandler handler) : IConsumer<ResourceIntegrationEvent>
{
    public Task Consume(ConsumeContext<ResourceIntegrationEvent> context) => handler.HandleAsync(UpstreamEventAdapter.Normalize(context.Message), context.CancellationToken);
}
public sealed class ReadinessEventConsumer(IntegrationEventIngestionHandler handler) : IConsumer<ReadinessIntegrationEvent>
{
    public Task Consume(ConsumeContext<ReadinessIntegrationEvent> context) => handler.HandleAsync(UpstreamEventAdapter.Normalize(context.Message), context.CancellationToken);
}
public sealed class ReportingEventConsumer(IntegrationEventIngestionHandler handler) : IConsumer<ReportingIntegrationEvent>
{
    public Task Consume(ConsumeContext<ReportingIntegrationEvent> context) => handler.HandleAsync(UpstreamEventAdapter.Normalize(context.Message), context.CancellationToken);
}

public sealed class IdentityEventConsumerDefinition : ConsumerDefinition<IdentityEventConsumer>
{
    public IdentityEventConsumerDefinition() { EndpointName = "notification-audit-identity-events"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<IdentityEventConsumer> consumer, IRegistrationContext context) => UpstreamEventRetry.Configure(endpoint);
}
public sealed class PlanningEventConsumerDefinition : ConsumerDefinition<PlanningEventConsumer>
{
    public PlanningEventConsumerDefinition() { EndpointName = "notification-audit-planning-events"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<PlanningEventConsumer> consumer, IRegistrationContext context) => UpstreamEventRetry.Configure(endpoint);
}
public sealed class ResourceEventConsumerDefinition : ConsumerDefinition<ResourceEventConsumer>
{
    public ResourceEventConsumerDefinition() { EndpointName = "notification-audit-resource-events"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<ResourceEventConsumer> consumer, IRegistrationContext context) => UpstreamEventRetry.Configure(endpoint);
}
public sealed class ReadinessEventConsumerDefinition : ConsumerDefinition<ReadinessEventConsumer>
{
    public ReadinessEventConsumerDefinition() { EndpointName = "notification-audit-readiness-events"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<ReadinessEventConsumer> consumer, IRegistrationContext context) => UpstreamEventRetry.Configure(endpoint);
}
public sealed class ReportingEventConsumerDefinition : ConsumerDefinition<ReportingEventConsumer>
{
    public ReportingEventConsumerDefinition() { EndpointName = "notification-audit-reporting-events"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<ReportingEventConsumer> consumer, IRegistrationContext context) => UpstreamEventRetry.Configure(endpoint);
}

internal static class UpstreamEventRetry
{
    public static void Configure(IReceiveEndpointConfigurator endpoint) => endpoint.UseMessageRetry(retry =>
    { retry.Ignore<InvalidNotificationEventException>(); retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
}
