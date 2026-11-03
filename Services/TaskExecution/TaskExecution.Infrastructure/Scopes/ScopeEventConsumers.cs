using MassTransit;
using Identity.Contracts.Events;
using ParishCoordination.Contracts.Events;
using TaskExecution.Application.Scopes;

namespace TaskExecution.Infrastructure.Scopes;

public sealed class PlanningScopeConsumer(PlanningScopeIngestionHandler handler) : IConsumer<PlanningScopeSnapshot>
{
    public Task Consume(ConsumeContext<PlanningScopeSnapshot> context) => handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class IdentityStatusConsumer(IdentityStatusIngestionHandler handler) : IConsumer<IdentityIntegrationEvent>
{
    public Task Consume(ConsumeContext<IdentityIntegrationEvent> context) => handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class PlanningScopeConsumerDefinition : ConsumerDefinition<PlanningScopeConsumer>
{
    public PlanningScopeConsumerDefinition() { EndpointName = "task-planning-scopes"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint,
        IConsumerConfigurator<PlanningScopeConsumer> consumer, IRegistrationContext context) =>
        endpoint.UseMessageRetry(retry =>
        {
            retry.Ignore<FormatException>();
            retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        });
}

public sealed class IdentityStatusConsumerDefinition : ConsumerDefinition<IdentityStatusConsumer>
{
    public IdentityStatusConsumerDefinition() { EndpointName = "task-identity-status"; ConcurrentMessageLimit = 4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint,
        IConsumerConfigurator<IdentityStatusConsumer> consumer, IRegistrationContext context) =>
        endpoint.UseMessageRetry(retry =>
        {
            retry.Ignore<FormatException>();
            retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        });
}
