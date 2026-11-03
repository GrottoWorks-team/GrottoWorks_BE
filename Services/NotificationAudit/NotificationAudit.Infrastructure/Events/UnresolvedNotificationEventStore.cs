using Microsoft.EntityFrameworkCore;
using NotificationAudit.Application.Events;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.Events;

public sealed class UnresolvedNotificationEventStore(NotificationAuditDbContext db) : IUnresolvedNotificationEventStore
{
    public async Task<IReadOnlyList<RetainedNotificationEvent>> GetPageAsync(DateTimeOffset cutoff,
        NotificationReplayCursor? after, int size, CancellationToken cancellationToken) =>
        await BuildPageQuery(db, cutoff, after, size)
            .Select(row => new RetainedNotificationEvent(row.EventId, row.EventType, row.ReceivedAt, row.SafeEnvelope!))
            .ToListAsync(cancellationToken);

    public static IQueryable<NotificationEventInbox> BuildPageQuery(NotificationAuditDbContext db, DateTimeOffset cutoff,
        NotificationReplayCursor? after, int size)
    {
        if (size is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(size));
        var query = db.NotificationEventInbox.AsNoTracking()
            .Where(row => row.Status == "REQUIRES_CONTEXT" && row.SafeEnvelope != null && row.ReceivedAt <= cutoff);
        if (after is not null)
            query = query.Where(row => row.ReceivedAt > after.ReceivedAt ||
                (row.ReceivedAt == after.ReceivedAt && row.EventId.CompareTo(after.EventId) > 0));
        return query.OrderBy(row => row.ReceivedAt).ThenBy(row => row.EventId).Take(size);
    }

    public async Task QuarantineAsync(Guid eventId, CancellationToken cancellationToken)
    {
        // A concurrently completed event must remain completed. No payload/error text is copied to logs.
        await db.NotificationEventInbox.Where(row => row.EventId == eventId && row.Status == "REQUIRES_CONTEXT")
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, "QUARANTINED")
                .SetProperty(row => row.LastError, "INVALID_RETAINED_CONTEXT"), cancellationToken);
    }
}
