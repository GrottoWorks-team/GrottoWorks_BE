using Microsoft.EntityFrameworkCore;
using Npgsql;
using NotificationAudit.Application.Events;
using NotificationAudit.Domain.Audit;
using NotificationAudit.Domain.Notifications;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.Events;

internal sealed class NotificationEventStore(
    NotificationAuditDbContext dbContext,
    TimeProvider timeProvider) : INotificationEventStore
{
    public async Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await dbContext.NotificationEventInbox.AnyAsync(item => item.EventId == plan.EventId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var now = timeProvider.GetUtcNow();
        dbContext.NotificationEventInbox.Add(NotificationEventInbox.Create(
            plan.EventId,
            plan.EventType,
            plan.Status,
            now));

        if (plan.Status == "PROCESSED")
        {
            var ruleEnabled = await dbContext.NotificationRules.AsNoTracking()
                .Where(rule => rule.EventType == plan.EventType)
                .Select(rule => (bool?)rule.InAppEnabled)
                .SingleOrDefaultAsync(cancellationToken) ?? false;

            if (ruleEnabled)
            {
                foreach (var candidate in plan.Notifications)
                {
                    var userEnabled = await dbContext.NotificationPreferences.AsNoTracking()
                        .Where(preference => preference.UserId == candidate.RecipientUserId)
                        .Select(preference => (bool?)preference.InAppEnabled)
                        .SingleOrDefaultAsync(cancellationToken) ?? true;
                    if (!userEnabled) continue;

                    dbContext.Notifications.Add(Notification.Create(
                        candidate.RecipientUserId,
                        plan.EventType,
                        candidate.Title,
                        candidate.Body,
                        candidate.ReferenceType,
                        candidate.ReferenceId,
                        plan.Audit.OccurredAt));
                }
            }
        }

        dbContext.ActivityLogs.Add(ActivityLog.Create(
            plan.Audit.ActorUserId,
            plan.Audit.ParishId,
            plan.Audit.SeasonId,
            plan.Audit.Action,
            plan.Audit.EntityType,
            plan.Audit.EntityId,
            null,
            plan.Audit.SafeNewValue,
            null,
            plan.Audit.CorrelationId,
            plan.Audit.OccurredAt));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateEvent(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static bool IsDuplicateEvent(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
