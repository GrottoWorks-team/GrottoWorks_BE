using MassTransit;
using Resource.Contracts.Events;
using TaskExecution.Application.Support;

namespace TaskExecution.Infrastructure.Support;

public sealed class SupportTransferReceiptConsumer(SupportTransferReceiptIngestion handler) : IConsumer<ResourceIntegrationEvent>
{
    public Task Consume(ConsumeContext<ResourceIntegrationEvent> context) => handler.HandleAsync(context.Message,context.CancellationToken);
}
public sealed class SupportTransferReceiptConsumerDefinition : ConsumerDefinition<SupportTransferReceiptConsumer>
{
    public SupportTransferReceiptConsumerDefinition() { EndpointName="task-support-transfer-receipts"; ConcurrentMessageLimit=4; }
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint,IConsumerConfigurator<SupportTransferReceiptConsumer> consumer,IRegistrationContext context) =>
        endpoint.UseMessageRetry(retry=>
        {
            retry.Ignore<FormatException>();
            retry.Intervals(TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(15));
        });
}
