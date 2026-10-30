using BuildingBlocks.Web.Exceptions;
using NotificationAudit.Contracts.Notifications;
using NotificationAudit.Domain.Notifications;

namespace NotificationAudit.Application.Notifications;

public interface INotificationRuleRepository
{
    Task<NotificationRule?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class NotificationRuleConcurrencyException : Exception;

public sealed class UpdateNotificationRuleHandler(
    INotificationRuleRepository repository,
    TimeProvider timeProvider)
{
    public async Task<NotificationRuleDto> HandleAsync(
        Guid ruleId,
        bool inAppEnabled,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetForUpdateAsync(ruleId, cancellationToken);
        if (rule is null)
        {
            throw new DomainException("NOTIFICATION_RULE_NOT_FOUND", "The notification rule does not exist.", 404);
        }

        if (expectedVersion.HasValue && expectedVersion.Value != rule.Version)
        {
            throw new DomainException("NOTIFICATION_RULE_VERSION_MISMATCH", "The notification rule has changed.", 412);
        }

        if (rule.Update(inAppEnabled, timeProvider.GetUtcNow()))
        {
            try
            {
                await repository.SaveChangesAsync(cancellationToken);
            }
            catch (NotificationRuleConcurrencyException)
            {
                throw new DomainException("NOTIFICATION_RULE_VERSION_MISMATCH", "The notification rule has changed.", 412);
            }
        }

        return new NotificationRuleDto(rule.Id, rule.EventType, rule.InAppEnabled, rule.Version, rule.UpdatedAt);
    }
}
