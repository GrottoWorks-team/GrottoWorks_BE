using MassTransit;
using NotificationAudit.Application.Events;
using TaskExecution.Contracts.Events;

namespace NotificationAudit.Infrastructure.Events;

public sealed class TaskIntegrationEventConsumer(IntegrationEventIngestionHandler handler)
    : IConsumer<TaskIntegrationEvent>
{
    public Task Consume(ConsumeContext<TaskIntegrationEvent> context)
    {
        var message = context.Message;
        return handler.HandleAsync(
            new IncomingIntegrationEvent(
                message.EventId,
                message.EventType,
                message.OccurredAt,
                message.CorrelationId,
                message.ActorId,
                message.ParishId,
                message.SeasonId,
                message.AggregateType,
                message.AggregateId,
                message.Payload,
                message.Version,
                message.AggregateVersion),
            context.CancellationToken);
    }
}

public sealed class TaskIntegrationEventConsumerDefinition
    : ConsumerDefinition<TaskIntegrationEventConsumer>
{
    public TaskIntegrationEventConsumerDefinition()
    {
        EndpointName = "notification-audit-task-events";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TaskIntegrationEventConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry =>
        {
            retry.Ignore<InvalidNotificationEventException>();
            retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        });
    }
}
