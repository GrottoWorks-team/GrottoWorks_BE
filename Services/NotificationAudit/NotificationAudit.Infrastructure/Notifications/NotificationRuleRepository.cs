using Microsoft.EntityFrameworkCore;
using NotificationAudit.Application.Notifications;
using NotificationAudit.Domain.Notifications;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.Notifications;

internal sealed class NotificationRuleRepository(NotificationAuditDbContext dbContext) : INotificationRuleRepository
{
    public Task<NotificationRule?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.NotificationRules.SingleOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new NotificationRuleConcurrencyException();
        }
    }
}

public interface INotificationRuleSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

internal sealed class NotificationRuleSeeder(NotificationAuditDbContext dbContext, TimeProvider timeProvider)
    : INotificationRuleSeeder
{
    public static readonly IReadOnlyDictionary<Guid, string> Catalog = new Dictionary<Guid, string>
    {
        [Guid.Parse("71000000-0000-0000-0000-000000000001")] = "task.assignment.created",
        [Guid.Parse("71000000-0000-0000-0000-000000000002")] = "task.assignment.responded",
        [Guid.Parse("71000000-0000-0000-0000-000000000003")] = "task.status.changed",
        [Guid.Parse("71000000-0000-0000-0000-000000000004")] = "attendance.request.submitted",
        [Guid.Parse("71000000-0000-0000-0000-000000000005")] = "attendance.request.reviewed",
        [Guid.Parse("71000000-0000-0000-0000-000000000006")] = "attendance.hours.approved",
        [Guid.Parse("71000000-0000-0000-0000-000000000007")] = "attendance.session.open-too-long",
        [Guid.Parse("71000000-0000-0000-0000-000000000008")] = "planning.season.activated",
        [Guid.Parse("71000000-0000-0000-0000-000000000009")] = "planning.season.completed",
        [Guid.Parse("71000000-0000-0000-0000-000000000010")] = "planning.season.archived",
        [Guid.Parse("71000000-0000-0000-0000-000000000011")] = "resource.purchase-request.decided",
        [Guid.Parse("71000000-0000-0000-0000-000000000012")] = "readiness.inspection.completed"
    };

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.NotificationRules.Select(rule => rule.Id).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var item in Catalog.Where(item => !existing.Contains(item.Key)))
        {
            dbContext.NotificationRules.Add(NotificationRule.Create(item.Key, item.Value, now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
