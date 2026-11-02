using MassTransit;
using Resource.Application.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.Infrastructure.Messaging;

/// <summary>TaskExecution outbox events → <c>task_projection</c> (enabled by <c>TaskEvents:Enabled</c>).</summary>
public sealed class TaskIntegrationEventConsumer(ApplyTaskEventHandler handler) : IConsumer<TaskIntegrationEvent>
{
    public Task Consume(ConsumeContext<TaskIntegrationEvent> context) => handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class TaskIntegrationEventConsumerDefinition : ConsumerDefinition<TaskIntegrationEventConsumer>
{
    public TaskIntegrationEventConsumerDefinition()
    {
        EndpointName = "resource-task-events";
        ConcurrentMessageLimit = 4;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TaskIntegrationEventConsumer> consumerConfigurator,
        IRegistrationContext context) =>
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
}
