using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
        var key = $"notification-inbox:{plan.EventId:D}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var existing = await dbContext.NotificationEventInbox.SingleOrDefaultAsync(item => item.EventId == plan.EventId, cancellationToken);
        var safeEnvelope = plan.SafeContext is null ? null : JsonSerializer.Serialize(plan.SafeContext, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (plan.Status is not ("PROCESSED" or "REQUIRES_CONTEXT") ||
            (plan.Status == "REQUIRES_CONTEXT" && safeEnvelope is null))
            throw new InvalidNotificationEventException("An unresolved event must retain its safe source context.");
        if (existing is not null)
        {
            if (existing.EventType != plan.EventType)
                throw new InvalidNotificationEventException("An event ID was reused for another event type.");
            var adoptedContext = existing.EnsureSameContext(safeEnvelope);
            if (existing.Status == "PROCESSED" || plan.Status == "REQUIRES_CONTEXT")
            {
                if (adoptedContext) await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }

        var now = timeProvider.GetUtcNow();
        var inbox = existing ?? NotificationEventInbox.Create(plan.EventId, plan.EventType, plan.Status, now, safeEnvelope);
        if (existing is null) dbContext.NotificationEventInbox.Add(inbox);

        if (plan.Status == "PROCESSED")
        {
            var ruleEnabled = await dbContext.NotificationRules.AsNoTracking()
                .Where(rule => rule.EventType == plan.EventType)
                .Select(rule => (bool?)rule.InAppEnabled)
                .SingleOrDefaultAsync(cancellationToken) ?? false;

            if (ruleEnabled)
            {
                var candidates = plan.Notifications.GroupBy(candidate => candidate.RecipientUserId)
                    .Select(group => group.First()).ToArray();
                var recipients = candidates.Select(candidate => candidate.RecipientUserId).ToArray();
                var preferences = await dbContext.NotificationPreferences.AsNoTracking()
                    .Where(preference => recipients.Contains(preference.UserId))
                    .ToDictionaryAsync(preference => preference.UserId, preference => preference.InAppEnabled, cancellationToken);
                foreach (var candidate in candidates)
                {
                    var userEnabled = preferences.GetValueOrDefault(candidate.RecipientUserId, true);
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

        // Resolving context later must not append the original audit entry a second time.
        if (existing is null) dbContext.ActivityLogs.Add(ActivityLog.Create(
            plan.Audit.ActorUserId,
            plan.Audit.ParishId,
            plan.Audit.SeasonId,
            plan.Audit.Action,
            plan.Audit.EntityType,
            plan.Audit.EntityId,
            plan.Audit.SafeOldValue,
            plan.Audit.SafeNewValue,
            plan.Audit.Reason,
            plan.Audit.CorrelationId,
            plan.Audit.OccurredAt));

        if (plan.Status == "PROCESSED") inbox.MarkProcessed(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

}
