using System.Text.Json;

namespace NotificationAudit.Application.Events;

public sealed record NotificationReplayCursor(DateTimeOffset ReceivedAt, Guid EventId);
public sealed record RetainedNotificationEvent(Guid EventId, string EventType, DateTimeOffset ReceivedAt, string SafeEnvelope);

public interface IUnresolvedNotificationEventStore
{
    Task<IReadOnlyList<RetainedNotificationEvent>> GetPageAsync(DateTimeOffset cutoff, NotificationReplayCursor? after,
        int size, CancellationToken cancellationToken);
    Task QuarantineAsync(Guid eventId, CancellationToken cancellationToken);
}

public sealed class ReplayNotificationContextHandler(IntegrationEventIngestionHandler ingestion)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(RetainedNotificationEvent row, CancellationToken cancellationToken)
    {
        IncomingIntegrationEvent message;
        try
        {
            message = JsonSerializer.Deserialize<IncomingIntegrationEvent>(row.SafeEnvelope, JsonOptions)
                ?? throw new InvalidNotificationEventException("The retained context is empty.");
        }
        catch (JsonException)
        {
            throw new InvalidNotificationEventException("The retained context is not a valid event envelope.");
        }

        if (message.EventId != row.EventId || message.EventType != row.EventType)
            throw new InvalidNotificationEventException("The retained context does not match its inbox identity.");

        // The original event ID and occurred time are preserved. StoreAsync owns the atomic deduplication boundary.
        await ingestion.HandleAsync(message, cancellationToken);
    }
}
