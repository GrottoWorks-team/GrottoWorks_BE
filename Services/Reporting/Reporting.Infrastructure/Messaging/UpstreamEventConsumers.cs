using MassTransit;
using Readiness.Contracts.Events;
using Reporting.Application.Ingestion;
using Resource.Contracts.Events;

namespace Reporting.Infrastructure.Messaging;

/// <summary>
/// Resource outbox events (purchases, donations, shortages) → contributions, points and read models; the same handler as
/// <c>POST /internal/events</c>, so the inbox makes redelivery harmless.
/// </summary>
public sealed class ResourceIntegrationEventConsumer(IntegrationEventHandler handler) : IConsumer<ResourceIntegrationEvent>
{
    public Task Consume(ConsumeContext<ResourceIntegrationEvent> context)
    {
        var message = context.Message;
        return handler.HandleAsync(
            new IncomingEvent(message.EventId, message.EventType, message.Version, message.OccurredAt, message.Payload, message.ParishId,
                message.SeasonId, message.AggregateId),
            context.CancellationToken);
    }
}

/// <summary>Readiness outbox events (item failed, area ready / reopened) → readiness read models.</summary>
public sealed class ReadinessIntegrationEventConsumer(IntegrationEventHandler handler) : IConsumer<ReadinessIntegrationEvent>
{
    public Task Consume(ConsumeContext<ReadinessIntegrationEvent> context)
    {
        var message = context.Message;
        return handler.HandleAsync(
            new IncomingEvent(message.EventId, message.EventType, message.Version, message.OccurredAt, message.Payload, message.ParishId,
                message.SeasonId, message.AggregateId),
            context.CancellationToken);
    }
}

public sealed class ResourceIntegrationEventConsumerDefinition : ConsumerDefinition<ResourceIntegrationEventConsumer>
{
    public ResourceIntegrationEventConsumerDefinition()
    {
        EndpointName = "reporting-resource-events";
        ConcurrentMessageLimit = 4;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ResourceIntegrationEventConsumer> consumerConfigurator,
        IRegistrationContext context) =>
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
}

public sealed class ReadinessIntegrationEventConsumerDefinition : ConsumerDefinition<ReadinessIntegrationEventConsumer>
{
    public ReadinessIntegrationEventConsumerDefinition()
    {
        EndpointName = "reporting-readiness-events";
        ConcurrentMessageLimit = 4;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ReadinessIntegrationEventConsumer> consumerConfigurator,
        IRegistrationContext context) =>
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
}
