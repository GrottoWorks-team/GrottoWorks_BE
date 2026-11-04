using RabbitMQ.Client;

internal static class ReportingErrorReplay
{
    public static async Task RunAsync(string target = "reporting-task-events")
    {
        // Deliberately fixed to the isolated acceptance vhost and repaired queue pairs.
        if (target is not ("reporting-task-events" or "parish-lifecycle-task-snapshots"))
            throw new ArgumentException("Queue is outside the replay allowlist.");
        var errorQueue = target + "_error";
        var factory = new ConnectionFactory
        {
            HostName = "localhost", Port = 5672, VirtualHost = "verify20261103",
            UserName = Environment.GetEnvironmentVariable("VERIFY_RABBIT_USER") ?? "grottoworks",
            Password = Environment.GetEnvironmentVariable("VERIFY_RABBIT_PASSWORD")
                ?? throw new InvalidOperationException("VERIFY_RABBIT_PASSWORD is required."),
            AutomaticRecoveryEnabled = false
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        var pending = await channel.MessageCountAsync(errorQueue);
        for (var index = 0u; index < pending; index++)
        {
            var message = await channel.BasicGetAsync(errorQueue, autoAck: false)
                ?? throw new InvalidOperationException("Error queue changed during replay.");
            var properties = new BasicProperties
            {
                ContentType = message.BasicProperties.ContentType,
                ContentEncoding = message.BasicProperties.ContentEncoding,
                Headers = message.BasicProperties.Headers,
                MessageId = message.BasicProperties.MessageId,
                CorrelationId = message.BasicProperties.CorrelationId,
                Type = message.BasicProperties.Type,
                Persistent = true
            };
            await channel.BasicPublishAsync("", target, mandatory: true, properties, message.Body);
            // Confirmed publish precedes acknowledgement; a crash can duplicate but cannot discard the source.
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false);
        }
        Console.WriteLine($"Replayed {pending} retained {target} messages with their original envelope IDs.");
    }
}
