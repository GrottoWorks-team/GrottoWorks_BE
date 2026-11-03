using MassTransit;
using Resource.Application.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.Infrastructure.Messaging;

public sealed class TaskMaterialEventConsumer(TaskMaterialEventIngestion handler) : IConsumer<TaskIntegrationEvent>
{
    public Task Consume(ConsumeContext<TaskIntegrationEvent> context) => handler.HandleAsync(context.Message, context.CancellationToken);
}
public sealed class TaskMaterialEventConsumerDefinition : ConsumerDefinition<TaskMaterialEventConsumer>
{
    public TaskMaterialEventConsumerDefinition() { EndpointName = "resource-task-materials"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<TaskMaterialEventConsumer> consumer, IRegistrationContext context) =>
        endpoint.UseMessageRetry(retry => { retry.Ignore<FormatException>(); retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
}
