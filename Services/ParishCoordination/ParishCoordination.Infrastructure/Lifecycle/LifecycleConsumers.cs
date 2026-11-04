using MassTransit;
using Readiness.Contracts.Events;
using TaskExecution.Contracts.Events;

namespace ParishCoordination.Infrastructure.Lifecycle;

public sealed class LifecycleTaskSnapshotConsumer(LifecycleEventProcessor processor) : IConsumer<TaskScopeSnapshot>
{
    public Task Consume(ConsumeContext<TaskScopeSnapshot> context) => processor.ReceiveAsync(context.Message, context.CancellationToken);
}

public sealed class LifecycleTaskStatusConsumer(LifecycleEventProcessor processor) : IConsumer<TaskIntegrationEvent>
{
    public Task Consume(ConsumeContext<TaskIntegrationEvent> context) => processor.ReceiveAsync(context.Message, context.CancellationToken);
}

public sealed class LifecycleReadinessConsumer(LifecycleEventProcessor processor) : IConsumer<ReadinessIntegrationEvent>
{
    public Task Consume(ConsumeContext<ReadinessIntegrationEvent> context) => processor.ReceiveAsync(context.Message, context.CancellationToken);
}

public sealed class LifecycleTaskSnapshotDefinition : ConsumerDefinition<LifecycleTaskSnapshotConsumer>
{
    public LifecycleTaskSnapshotDefinition() { EndpointName = "parish-lifecycle-task-snapshots"; ConcurrentMessageLimit = 1; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<LifecycleTaskSnapshotConsumer> consumer, IRegistrationContext context) =>
        endpoint.UseMessageRetry(retry => { retry.Ignore<FormatException>(); retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
}

public sealed class LifecycleTaskStatusDefinition : ConsumerDefinition<LifecycleTaskStatusConsumer>
{
    public LifecycleTaskStatusDefinition() { EndpointName = "parish-lifecycle-task-status"; ConcurrentMessageLimit = 1; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<LifecycleTaskStatusConsumer> consumer, IRegistrationContext context) =>
        endpoint.UseMessageRetry(retry => { retry.Ignore<FormatException>(); retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
}

public sealed class LifecycleReadinessDefinition : ConsumerDefinition<LifecycleReadinessConsumer>
{
    public LifecycleReadinessDefinition() { EndpointName = "parish-lifecycle-readiness"; ConcurrentMessageLimit = 1; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<LifecycleReadinessConsumer> consumer, IRegistrationContext context) =>
        endpoint.UseMessageRetry(retry => { retry.Ignore<FormatException>(); retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
}
