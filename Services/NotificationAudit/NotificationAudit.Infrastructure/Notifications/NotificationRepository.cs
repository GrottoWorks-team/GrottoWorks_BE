using Microsoft.EntityFrameworkCore;
using NotificationAudit.Application.Notifications;
using NotificationAudit.Domain.Notifications;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.Notifications;

internal sealed class NotificationRepository(NotificationAuditDbContext dbContext) : INotificationRepository
{
    public async Task<(IReadOnlyList<Notification> Items, long Total)> GetPageAsync(
        Guid recipientUserId,
        bool? isRead,
        string? eventType,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Notifications.AsNoTracking()
            .Where(notification => notification.RecipientUserId == recipientUserId);
        if (isRead.HasValue) query = query.Where(notification => notification.IsRead == isRead.Value);
        if (eventType is not null) query = query.Where(notification => notification.EventType == eventType);

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<bool> MarkReadAsync(Guid id, Guid recipientUserId, DateTimeOffset readAt, CancellationToken cancellationToken)
    {
        var owned = dbContext.Notifications.Where(notification => notification.Id == id && notification.RecipientUserId == recipientUserId);
        var changed = await owned.Where(notification => !notification.IsRead).ExecuteUpdateAsync(setters => setters
            .SetProperty(notification => notification.IsRead, true)
            .SetProperty(notification => notification.ReadAt, notification => notification.ReadAt ?? readAt), cancellationToken);
        return changed > 0 || await owned.AnyAsync(cancellationToken);
    }

    public async Task MarkAllReadAsync(
        Guid recipientUserId,
        DateTimeOffset cutoff,
        DateTimeOffset readAt,
        CancellationToken cancellationToken)
    {
        // Freeze membership before updating so late/backdated events are not marked by this operation.
        var ids = await dbContext.Notifications.AsNoTracking()
            .Where(notification => notification.RecipientUserId == recipientUserId &&
                                   !notification.IsRead && notification.CreatedAt <= cutoff)
            .Select(notification => notification.Id).ToArrayAsync(cancellationToken);
        if (ids.Length == 0) return;
        await dbContext.Notifications
            .Where(notification => notification.RecipientUserId == recipientUserId && ids.Contains(notification.Id) && !notification.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.IsRead, true)
                .SetProperty(notification => notification.ReadAt, notification => notification.ReadAt ?? readAt), cancellationToken);
    }
}
