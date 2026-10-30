using Microsoft.EntityFrameworkCore;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.TaskScopeSync;

public sealed class TaskScopeBackfill(ParishCoordinationDbContext db)
{
    public async Task EnqueueCurrentAsync(CancellationToken cancellationToken, int batchSize = 100)
    {
        Guid? lastId = null;
        while (true)
        {
            var seasons = await db.Seasons.AsNoTracking()
                .Where(season => !lastId.HasValue || season.Id.CompareTo(lastId.Value) > 0)
                .OrderBy(season => season.Id).Take(batchSize).ToListAsync(cancellationToken);
            if (seasons.Count == 0) break;
            foreach (var season in seasons)
                await EnqueueAsync(TaskScopeOutbox.ForSeason(season), cancellationToken);
            lastId = seasons[^1].Id;
        }

        lastId = null;
        while (true)
        {
            var areas = await db.WorkAreas.AsNoTracking()
                .Where(area => !lastId.HasValue || area.Id.CompareTo(lastId.Value) > 0)
                .OrderBy(area => area.Id).Take(batchSize).ToListAsync(cancellationToken);
            if (areas.Count == 0) break;
            var seasonIds = areas.Select(area => area.SeasonId).Distinct().ToArray();
            var seasons = await db.Seasons.AsNoTracking().Where(season => seasonIds.Contains(season.Id))
                .ToDictionaryAsync(season => season.Id, cancellationToken);
            foreach (var area in areas)
                await EnqueueAsync(TaskScopeOutbox.ForWorkArea(area, seasons[area.SeasonId]), cancellationToken);
            lastId = areas[^1].Id;
        }
    }

    private Task EnqueueAsync(TaskScopeOutbox message, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO task_scope_outbox
                (id, scope_type, aggregate_id, source_version, payload, created_at, attempts, next_attempt_at)
            VALUES ({message.Id}, {message.ScopeType}, {message.AggregateId}, {message.SourceVersion},
                CAST({message.Payload} AS jsonb), {message.CreatedAt}, 0, {message.NextAttemptAt})
            ON CONFLICT (scope_type, aggregate_id, source_version)
            DO UPDATE SET delivered_at = NULL, next_attempt_at = EXCLUDED.next_attempt_at
            """, cancellationToken);
}
