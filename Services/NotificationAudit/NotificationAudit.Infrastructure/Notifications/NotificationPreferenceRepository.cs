using Microsoft.EntityFrameworkCore;
using NotificationAudit.Application.Notifications;
using NotificationAudit.Domain.Notifications;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.Notifications;

internal sealed class NotificationPreferenceRepository(NotificationAuditDbContext dbContext)
    : INotificationPreferenceRepository
{
    public Task<NotificationPreference?> GetAsync(Guid userId, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking
            ? dbContext.NotificationPreferences.AsQueryable()
            : dbContext.NotificationPreferences.AsNoTracking();
        return query.SingleOrDefaultAsync(preference => preference.UserId == userId, cancellationToken);
    }

    public void Add(NotificationPreference preference) => dbContext.NotificationPreferences.Add(preference);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
