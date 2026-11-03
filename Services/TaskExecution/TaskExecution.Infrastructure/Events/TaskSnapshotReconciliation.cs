using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Events;

public sealed record TaskSnapshotBatch(Guid? LastId, int Scanned, int Scheduled, int MissingParents);

public sealed class TaskSnapshotReconciliation(TaskExecutionDbContext db, TimeProvider clock)
{
    public async Task<TaskSnapshotBatch> EnqueueBatchAsync(Guid? afterId, int size, CancellationToken cancellationToken)
    {
        if (size is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(size));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71110302)", cancellationToken);
        // Parent, task, assignment and material state must come from the same MVCC snapshot.
        var tasks = await db.Tasks.AsNoTracking().Include(task => task.Materials)
            .Where(task => !afterId.HasValue || task.Id.CompareTo(afterId.Value) > 0)
            .OrderBy(task => task.Id).Take(size).ToListAsync(cancellationToken);
        if (tasks.Count == 0) { await transaction.CommitAsync(cancellationToken); return new(afterId, 0, 0, 0); }
        var ids = tasks.Select(task => task.Id).ToArray();
        var areaIds = tasks.Select(task => task.WorkAreaId).Distinct().ToArray();
        var areas = await db.WorkAreaScopes.AsNoTracking().Where(area => areaIds.Contains(area.WorkAreaId))
            .ToDictionaryAsync(area => area.WorkAreaId, cancellationToken);
        var seasonIds = areas.Values.Select(area => area.SeasonId).Distinct().ToArray();
        var seasons = await db.SeasonScopes.AsNoTracking().Where(season => seasonIds.Contains(season.SeasonId))
            .ToDictionaryAsync(season => season.SeasonId, cancellationToken);
        var assignments = await db.Assignments.AsNoTracking().Where(row => ids.Contains(row.TaskId)).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var missingParents = 0;
        var messages = new List<TaskEventOutboxMessage>(tasks.Count);
        foreach (var task in tasks)
        {
            if (!areas.TryGetValue(task.WorkAreaId, out var area) || !seasons.TryGetValue(area.SeasonId, out var season))
            { missingParents++; continue; }
            messages.Add(TaskEventOutboxMessage.Create(TaskSnapshotMapper.Map(task, area, season, assignments, now)));
        }
        var messageIds = messages.Select(message => message.Id).ToArray();
        var existing = await db.EventOutbox.Where(row => messageIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        var scheduled = 0;
        foreach (var message in messages)
        {
            if (existing.TryGetValue(message.Id, out var row))
            { if (row.ScheduleSnapshotReplay(now)) scheduled++; }
            else { db.EventOutbox.Add(message); scheduled++; }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(tasks[^1].Id, tasks.Count, scheduled, missingParents);
    }
}

public sealed class TaskSnapshotReconciliationWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<TaskSnapshotReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Outbox:ReconcileIntervalMinutes", 30), 1, 1440));
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = interval;
            try
            {
                Guid? cursor = null;
                var scheduled = 0;
                var missing = 0;
                while (true)
                {
                    using var scope = scopes.CreateScope();
                    var batch = await scope.ServiceProvider.GetRequiredService<TaskSnapshotReconciliation>()
                        .EnqueueBatchAsync(cursor, 100, stoppingToken);
                    scheduled += batch.Scheduled;
                    missing += batch.MissingParents;
                    if (batch.Scanned == 0) break;
                    cursor = batch.LastId;
                }
                logger.LogInformation("Task snapshot reconciliation scheduled {Count} snapshots; {MissingParents} tasks await parent scopes.", scheduled, missing);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning("Task snapshot reconciliation failed ({ErrorType}); retrying.", exception.GetType().Name);
                delay = TimeSpan.FromSeconds(5);
            }
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
