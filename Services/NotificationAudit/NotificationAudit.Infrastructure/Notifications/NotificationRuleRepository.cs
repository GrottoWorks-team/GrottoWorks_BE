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
    public static IReadOnlyDictionary<Guid, string> Catalog => NotificationRuleCatalog.All;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71110308)", cancellationToken);
        var existing = await dbContext.NotificationRules.Select(rule => rule.Id).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var item in Catalog.Where(item => !existing.Contains(item.Key)))
        {
            dbContext.NotificationRules.Add(NotificationRule.Create(item.Key, item.Value, now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
