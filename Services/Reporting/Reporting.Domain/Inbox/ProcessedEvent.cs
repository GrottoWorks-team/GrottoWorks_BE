namespace Reporting.Domain.Inbox;

/// <summary>
/// Inbox row (technical, spec §3 "outbox/inbox"): an integration event already applied. Written in the same transaction
/// as its effects, so a redelivered event is acknowledged without being applied twice (BR-55).
/// </summary>
public sealed class ProcessedEvent
{
    private ProcessedEvent()
    {
    }

    public Guid EventId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Outcome { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }

    public static ProcessedEvent Record(Guid eventId, string eventType, string outcome, DateTimeOffset now) =>
        new() { EventId = eventId, EventType = eventType, Outcome = outcome, ProcessedAt = now.ToUniversalTime() };
}
