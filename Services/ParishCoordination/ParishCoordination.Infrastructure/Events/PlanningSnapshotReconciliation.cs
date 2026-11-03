using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Events;

public sealed record PlanningSnapshotBatch(Guid? LastId, int Scanned, int Scheduled);

public sealed class PlanningSnapshotReconciliation(ParishCoordinationDbContext db, TimeProvider clock)
{
    public async Task<PlanningSnapshotBatch> EnqueueBatchAsync(bool workAreas, Guid? afterId, int size, CancellationToken cancellationToken)
    {
        if (size is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(size));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71110304)", cancellationToken);
        var messages = new List<PlanningEventOutboxMessage>(size);
        Guid? lastId = afterId;
        if (!workAreas)
        {
            var seasons = await db.Seasons.AsNoTracking().Where(season => !afterId.HasValue || season.Id.CompareTo(afterId.Value) > 0)
                .OrderBy(season => season.Id).Take(size).ToListAsync(cancellationToken);
            foreach (var season in seasons) messages.Add(PlanningEventOutboxMessage.Create(PlanningSnapshotMapper.Scope(season)));
            if (seasons.Count > 0) lastId = seasons[^1].Id;
        }
        else
        {
            var areas = await db.WorkAreas.AsNoTracking().Where(area => !afterId.HasValue || area.Id.CompareTo(afterId.Value) > 0)
                .OrderBy(area => area.Id).Take(size).ToListAsync(cancellationToken);
            var seasonIds = areas.Select(area => area.SeasonId).Distinct().ToArray();
            var seasons = await db.Seasons.AsNoTracking().Where(season => seasonIds.Contains(season.Id))
                .ToDictionaryAsync(season => season.Id, cancellationToken);
            foreach (var area in areas)
            {
                if (!seasons.TryGetValue(area.SeasonId, out var season))
                    throw new InvalidOperationException("A Work Area has no owning Season.");
                messages.Add(PlanningEventOutboxMessage.Create(PlanningSnapshotMapper.Scope(season, area)));
            }
            if (areas.Count > 0) lastId = areas[^1].Id;
        }
        if (messages.Count == 0) { await transaction.CommitAsync(cancellationToken); return new(afterId, 0, 0); }
        var aggregateIds = messages.Select(message => message.AggregateId).ToArray();
        var versions = messages.Select(message => message.SourceVersion).Distinct().ToArray();
        var existing = await db.PlanningEventOutbox.Where(row => row.EventType == "scope.snapshot" &&
                aggregateIds.Contains(row.AggregateId) && versions.Contains(row.SourceVersion))
            .ToDictionaryAsync(row => (row.AggregateId, row.SourceVersion), cancellationToken);
        var scheduled = 0;
        var now = clock.GetUtcNow();
        foreach (var message in messages)
        {
            if (existing.TryGetValue((message.AggregateId, message.SourceVersion), out var row))
            { if (row.ScheduleSnapshotReplay(now)) scheduled++; }
            else { db.PlanningEventOutbox.Add(message); scheduled++; }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(lastId, messages.Count, scheduled);
    }
}

public sealed class PlanningSnapshotReconciliationWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<PlanningSnapshotReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("PlanningEvents:ReconcileIntervalMinutes", 30), 1, 1440));
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = interval;
            try
            {
                var scheduled = 0;
                // Enqueue parent snapshots before child snapshots; receivers also enforce source version.
                foreach (var workAreas in new[] { false, true })
                {
                    Guid? cursor = null;
                    while (true)
                    {
                        using var scope = scopes.CreateScope();
                        var batch = await scope.ServiceProvider.GetRequiredService<PlanningSnapshotReconciliation>()
                            .EnqueueBatchAsync(workAreas, cursor, 100, stoppingToken);
                        scheduled += batch.Scheduled;
                        if (batch.Scanned == 0) break;
                        cursor = batch.LastId;
                    }
                }
                logger.LogInformation("Planning snapshot reconciliation scheduled {Count} snapshots.", scheduled);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning("Planning snapshot reconciliation failed ({ErrorType}); retrying.", exception.GetType().Name);
                delay = TimeSpan.FromSeconds(5);
            }
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
