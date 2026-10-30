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

    public Task<Notification?> GetForUpdateAsync(
        Guid id,
        Guid recipientUserId,
        CancellationToken cancellationToken) =>
        dbContext.Notifications.SingleOrDefaultAsync(
            notification => notification.Id == id && notification.RecipientUserId == recipientUserId,
            cancellationToken);

    public Task MarkAllReadAsync(
        Guid recipientUserId,
        DateTimeOffset cutoff,
        DateTimeOffset readAt,
        CancellationToken cancellationToken) =>
        dbContext.Notifications
            .Where(notification => notification.RecipientUserId == recipientUserId &&
                                   !notification.IsRead && notification.CreatedAt <= cutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.IsRead, true)
                .SetProperty(notification => notification.ReadAt, readAt), cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
