using MassTransit;
using Reporting.Application.Ingestion;
using TaskExecution.Contracts.Events;

namespace Reporting.Infrastructure.Messaging;

/// <summary>
/// TaskExecution outbox events → contributions, points and read models (enabled by <c>TaskEvents:Enabled</c>); the same
/// handler as <c>POST /internal/events</c>, so the inbox makes redelivery harmless and a 409 concurrent update is retried.
/// </summary>
public sealed class TaskIntegrationEventConsumer(IntegrationEventHandler handler) : IConsumer<TaskIntegrationEvent>
{
    public Task Consume(ConsumeContext<TaskIntegrationEvent> context)
    {
        var message = context.Message;
        return handler.HandleAsync(
            new IncomingEvent(message.EventId, message.EventType, message.Version, message.OccurredAt, message.Payload, message.ParishId,
                message.SeasonId, message.AggregateId, message.AggregateVersion),
            context.CancellationToken);
    }
}

public sealed class TaskIntegrationEventConsumerDefinition : ConsumerDefinition<TaskIntegrationEventConsumer>
{
    public TaskIntegrationEventConsumerDefinition()
    {
        EndpointName = "reporting-task-events";
        ConcurrentMessageLimit = 4;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TaskIntegrationEventConsumer> consumerConfigurator,
        IRegistrationContext context) =>
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
}
