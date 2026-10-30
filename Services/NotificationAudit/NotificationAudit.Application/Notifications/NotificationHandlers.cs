using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using NotificationAudit.Contracts.Notifications;
using NotificationAudit.Domain.Notifications;

namespace NotificationAudit.Application.Notifications;

public interface INotificationRepository
{
    Task<(IReadOnlyList<Notification> Items, long Total)> GetPageAsync(
        Guid recipientUserId,
        bool? isRead,
        string? eventType,
        int page,
        int size,
        CancellationToken cancellationToken);
    Task<Notification?> GetForUpdateAsync(Guid id, Guid recipientUserId, CancellationToken cancellationToken);
    Task MarkAllReadAsync(Guid recipientUserId, DateTimeOffset cutoff, DateTimeOffset readAt, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class ListNotificationsHandler(ICurrentUser currentUser, INotificationRepository repository)
{
    public async Task<(IReadOnlyList<NotificationDto> Items, long Total)> HandleAsync(
        bool? isRead,
        string? eventType,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetPageAsync(
            currentUser.UserId,
            isRead,
            string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(),
            page,
            size,
            cancellationToken);
        return (result.Items.Select(Map).ToList(), result.Total);
    }

    internal static NotificationDto Map(Notification notification) => new(
        notification.Id,
        notification.EventType,
        notification.Title,
        notification.Body,
        notification.ReferenceType,
        notification.ReferenceId,
        notification.IsRead,
        notification.CreatedAt,
        notification.ReadAt);
}

public sealed class MarkNotificationReadHandler(
    ICurrentUser currentUser,
    INotificationRepository repository,
    TimeProvider timeProvider)
{
    public async Task HandleAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await repository.GetForUpdateAsync(
            notificationId,
            currentUser.UserId,
            cancellationToken);
        if (notification is null)
        {
            throw new DomainException("NOTIFICATION_NOT_FOUND", "The notification does not exist.", 404);
        }

        if (notification.MarkRead(timeProvider.GetUtcNow()))
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class MarkAllNotificationsReadHandler(
    ICurrentUser currentUser,
    INotificationRepository repository,
    TimeProvider timeProvider)
{
    public Task HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return repository.MarkAllReadAsync(currentUser.UserId, now, now, cancellationToken);
    }
}
