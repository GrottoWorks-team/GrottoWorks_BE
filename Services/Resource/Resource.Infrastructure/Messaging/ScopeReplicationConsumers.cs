using MassTransit;
using ParishCoordination.Contracts.Events;
using Resource.Application.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.Infrastructure.Messaging;

public sealed class PlanningScopeConsumer(ScopeReplicationHandler handler) : IConsumer<PlanningScopeSnapshot>
{
    public Task Consume(ConsumeContext<PlanningScopeSnapshot> context) => handler.ApplyAsync(context.Message, context.CancellationToken);
}

public sealed class TaskScopeConsumer(ScopeReplicationHandler handler) : IConsumer<TaskScopeSnapshot>
{
    public Task Consume(ConsumeContext<TaskScopeSnapshot> context) => handler.ApplyAsync(context.Message, context.CancellationToken);
}

public sealed class PlanningScopeConsumerDefinition : ConsumerDefinition<PlanningScopeConsumer>
{
    public PlanningScopeConsumerDefinition() { EndpointName = "resource-planning-scopes"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<PlanningScopeConsumer> consumer, IRegistrationContext context) =>
        endpoint.UseMessageRetry(retry => { retry.Ignore<FormatException>(); retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
}

public sealed class TaskScopeConsumerDefinition : ConsumerDefinition<TaskScopeConsumer>
{
    public TaskScopeConsumerDefinition() { EndpointName = "resource-task-snapshots"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<TaskScopeConsumer> consumer, IRegistrationContext context) =>
        endpoint.UseMessageRetry(retry => { retry.Ignore<FormatException>(); retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
}
