using Microsoft.EntityFrameworkCore;
using NotificationAudit.Application.Audit;
using NotificationAudit.Domain.Audit;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.Audit;

internal sealed class ActivityLogRepository(NotificationAuditDbContext dbContext) : IActivityLogRepository
{
    public async Task<(IReadOnlyList<ActivityLog> Items, long Total)> GetPageAsync(
        ActivityLogFilter filter,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ActivityLogs.AsNoTracking().AsQueryable();
        if (filter.ActorUserId.HasValue) query = query.Where(log => log.ActorUserId == filter.ActorUserId);
        if (filter.ParishId.HasValue) query = query.Where(log => log.ParishId == filter.ParishId);
        if (filter.SeasonId.HasValue) query = query.Where(log => log.SeasonId == filter.SeasonId);
        if (!string.IsNullOrWhiteSpace(filter.Action)) query = query.Where(log => log.Action == filter.Action);
        if (!string.IsNullOrWhiteSpace(filter.EntityType)) query = query.Where(log => log.EntityType == filter.EntityType);
        if (filter.EntityId.HasValue) query = query.Where(log => log.EntityId == filter.EntityId);
        if (filter.From.HasValue) query = query.Where(log => log.CreatedAt >= filter.From);
        if (filter.To.HasValue) query = query.Where(log => log.CreatedAt < filter.To);

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(log => log.CreatedAt).ThenByDescending(log => log.Id)
            .Skip((filter.Page - 1) * filter.Size).Take(filter.Size).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<ActivityLog?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ActivityLogs.AsNoTracking().SingleOrDefaultAsync(log => log.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ActivityLog>> GetHistoryAsync(
        string entityType,
        Guid entityId,
        DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ActivityLogs.AsNoTracking().Where(log => log.EntityType == entityType && log.EntityId == entityId);
        if (from.HasValue) query = query.Where(log => log.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(log => log.CreatedAt < to.Value);
        return await query.OrderBy(log => log.CreatedAt).ThenBy(log => log.Id).ToListAsync(cancellationToken);
    }
}
