using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;
using Readiness.Contracts.Events;
using TaskExecution.Contracts.Events;

namespace ParishCoordination.Infrastructure.Lifecycle;

/// <summary>Durable ingestion and recovery. Business transitions are owned by the domain entities.</summary>
public sealed class LifecycleEventProcessor(ParishCoordinationDbContext db, IWorkAreaTaskCountClient counts, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Statuses = ["OPEN", "NOT_STARTED", "IN_PROGRESS", "SUBMITTED", "REVISION_REQUIRED", "COMPLETED", "CANCELLED"];

    public Task ReceiveAsync(TaskScopeSnapshot message, CancellationToken cancellationToken)
    {
        if (message.SchemaVersion != 1 || message.TaskId == Guid.Empty || message.SourceVersion < 1 ||
            message.Season is null || message.WorkArea is null || message.WorkAreaId != message.WorkArea.WorkAreaId ||
            message.WorkArea.SeasonId != message.Season.SeasonId || message.WorkArea.ParishId != message.Season.ParishId ||
            message.TaskType is not ("WORK" or "PURCHASE" or "CORRECTIVE"))
            throw new FormatException("Invalid Task scope snapshot.");
        ValidateStatus(message.Status);
        return ReceiveAsync(NewInbox(message.EventId, message.WorkAreaId, message.Season.SeasonId,
            message.Season.ParishId, "task.snapshot", message), cancellationToken);
    }

    public Task ReceiveAsync(TaskIntegrationEvent message, CancellationToken cancellationToken)
    {
        if (message.EventType != TaskEventTypes.StatusChanged) return Task.CompletedTask;
        if (message.Version != 1 || message.AggregateType != "task" || message.AggregateVersion < 1 ||
            ReadGuid(message.Payload, "taskId") != message.AggregateId)
            throw new FormatException("Invalid Task status event.");
        ValidateStatus(ReadString(message.Payload, "status"));
        return ReceiveAsync(NewInbox(message.EventId, ReadGuid(message.Payload, "workAreaId"), message.SeasonId,
            message.ParishId, "task.status", message), cancellationToken);
    }

    public Task ReceiveAsync(ReadinessIntegrationEvent message, CancellationToken cancellationToken)
    {
        if (message.EventType is not (ReadinessAreaReady.EventType or ReadinessAreaReopened.EventType)) return Task.CompletedTask;
        if (message.Version != 1 || message.ParishId is null || message.SeasonId is null)
            throw new FormatException("Invalid Readiness event envelope.");
        var areaId = ReadGuid(message.Payload, "workAreaId");
        if (ReadGuid(message.Payload, "seasonId") != message.SeasonId || ReadGuid(message.Payload, "parishId") != message.ParishId)
            throw new FormatException("Readiness event ownership is inconsistent.");
        var reopened = message.EventType == ReadinessAreaReopened.EventType;
        ReadGuid(message.Payload, "readinessChecklistId");
        ReadTimestamp(message.Payload, reopened ? "reopenedAt" : "confirmedAt");
        if (reopened && string.IsNullOrWhiteSpace(ReadString(message.Payload, "reason")))
            throw new FormatException("Readiness reopening requires a reason.");
        return ReceiveAsync(NewInbox(message.EventId, areaId, message.SeasonId.Value, message.ParishId.Value,
            "readiness", message), cancellationToken);
    }

    private LifecycleInboxMessage NewInbox<T>(Guid id, Guid areaId, Guid seasonId, Guid parishId, string kind, T message)
    {
        if (id == Guid.Empty || areaId == Guid.Empty || seasonId == Guid.Empty || parishId == Guid.Empty)
            throw new FormatException("Lifecycle event identifiers must not be empty.");
        return new() { EventId = id, WorkAreaId = areaId, SeasonId = seasonId, ParishId = parishId,
            Kind = kind, Envelope = JsonSerializer.Serialize(message, Json), ReceivedAt = clock.GetUtcNow() };
    }

    private async Task ReceiveAsync(LifecycleInboxMessage incoming, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(cancellationToken);
        var stored = await db.Set<LifecycleInboxMessage>().SingleOrDefaultAsync(x => x.EventId == incoming.EventId, cancellationToken);
        if (stored is not null)
        {
            if (stored.WorkAreaId != incoming.WorkAreaId || stored.Kind != incoming.Kind || stored.Envelope != incoming.Envelope)
                throw new FormatException("An event ID was reused for a different lifecycle message.");
            if (stored.ProcessedAt.HasValue) { await transaction.CommitAsync(cancellationToken); return; }
        }
        else db.Add(incoming);

        await ApplyAsync(stored ?? incoming, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecoverAreaAsync(Guid areaId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(cancellationToken);
        var pending = await db.Set<LifecycleInboxMessage>().Where(x => x.WorkAreaId == areaId && x.ProcessedAt == null)
            .OrderBy(x => x.ReceivedAt).ThenBy(x => x.EventId).Take(100).ToListAsync(cancellationToken);
        foreach (var row in pending) await ApplyAsync(row, cancellationToken);
        // Also retry derived transitions after count dependency failure or a parent activation.
        await ReconcileAreaAsync(areaId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task LockAsync(CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71110305)", cancellationToken)
            : Task.CompletedTask;

    private async Task ApplyAsync(LifecycleInboxMessage row, CancellationToken cancellationToken)
    {
        var area = await db.WorkAreas.SingleOrDefaultAsync(x => x.Id == row.WorkAreaId, cancellationToken);
        var season = await db.Seasons.SingleOrDefaultAsync(x => x.Id == row.SeasonId, cancellationToken);
        if (area is null || season is null) return; // Retained, pending; recovery will retry after the parent exists.
        if (area.SeasonId != season.Id || season.ParishId != row.ParishId)
            throw new FormatException("Lifecycle event is outside its owning Work Area/Season/Parish.");
        if (season.Status is SeasonStatus.Completed or SeasonStatus.Archived || area.Status == WorkAreaStatus.Completed)
        {
            row.ProcessedAt = clock.GetUtcNow(); // Terminal business state stays immutable.
            return;
        }

        if (row.Kind == "readiness")
        {
            var message = JsonSerializer.Deserialize<ReadinessIntegrationEvent>(row.Envelope, Json)!;
            var reopened = message.EventType == ReadinessAreaReopened.EventType;
            var readiness = await GetReadinessAsync(area.Id, cancellationToken);
            readiness.Apply(ReadGuid(message.Payload, "readinessChecklistId"),
                ReadTimestamp(message.Payload, reopened ? "reopenedAt" : "confirmedAt"), reopened);
        }
        else
        {
            Guid taskId;
            long version;
            string status;
            string? type;
            if (row.Kind == "task.snapshot")
            {
                var message = JsonSerializer.Deserialize<TaskScopeSnapshot>(row.Envelope, Json)!;
                (taskId, version, status, type) = (message.TaskId, message.SourceVersion, message.Status, message.TaskType);
            }
            else
            {
                var message = JsonSerializer.Deserialize<TaskIntegrationEvent>(row.Envelope, Json)!;
                (taskId, version, status, type) = (message.AggregateId, message.AggregateVersion, ReadString(message.Payload, "status"),
                    message.Payload.TryGetProperty("taskType", out var taskType) ? taskType.GetString() : null);
            }
            var task = await db.Set<LifecycleTaskProjection>().FindAsync([taskId], cancellationToken);
            if (task is null)
            {
                task = new() { TaskId = taskId, WorkAreaId = area.Id, SeasonId = season.Id, ParishId = season.ParishId };
                db.Add(task);
            }
            if (task.WorkAreaId != area.Id || task.SeasonId != season.Id || task.ParishId != season.ParishId)
                throw new FormatException("Task projection cannot move between Work Areas.");
            if (task.Apply(version, status, type) && status != "COMPLETED" && status != "CANCELLED")
            {
                var readiness = await db.Set<LifecycleReadinessProjection>().FindAsync([area.Id], cancellationToken);
                if (readiness is not null)
                {
                    // Newly unfinished work revokes an already consumed confirmation.
                    if (readiness.Applied || area.Status == WorkAreaStatus.Ready) readiness.Invalidated = true;
                    // Corrective work re-enters execution, but the old confirmation cannot be reused.
                    if (type == "CORRECTIVE" && readiness.Reopened) { readiness.Reopened = false; readiness.Invalidated = true; }
                }
            }
        }
        // Counts are read from the tracked projection below; no separate commit is needed.
        await ReconcileAreaAsync(area.Id, cancellationToken);
        row.ProcessedAt = clock.GetUtcNow();
    }

    private async Task<LifecycleReadinessProjection> GetReadinessAsync(Guid areaId, CancellationToken cancellationToken)
    {
        var row = await db.Set<LifecycleReadinessProjection>().FindAsync([areaId], cancellationToken);
        if (row is not null) return row;
        row = new() { WorkAreaId = areaId };
        db.Add(row);
        return row;
    }

    private async Task ReconcileAreaAsync(Guid areaId, CancellationToken cancellationToken)
    {
        var area = await db.WorkAreas.FindAsync([areaId], cancellationToken);
        if (area is null) return;
        var season = await db.Seasons.FindAsync([area.SeasonId], cancellationToken);
        if (season is null || season.Status is not (SeasonStatus.Active or SeasonStatus.Ready)) return;
        var persisted = await db.Set<LifecycleTaskProjection>().Where(x => x.WorkAreaId == areaId).ToListAsync(cancellationToken);
        var tasks = persisted.Concat(db.Set<LifecycleTaskProjection>().Local.Where(x => x.WorkAreaId == areaId))
            .DistinctBy(x => x.TaskId).ToList();
        var eligible = tasks.LongCount(x => x.Status != "CANCELLED");
        var completed = tasks.LongCount(x => x.Status == "COMPLETED");
        var started = tasks.Any(x => x.Status is "IN_PROGRESS" or "SUBMITTED" or "REVISION_REQUIRED" or "COMPLETED");
        var verified = false;
        if (eligible > 0 && eligible == completed)
        {
            // Snapshot delivery is incremental: one completed task cannot hide another OPEN task in the source.
            WorkAreaTaskCounts source;
            try { source = await counts.GetAsync(areaId, cancellationToken); }
            catch (WorkAreaTaskCountUnavailableException)
            {
                // Keep the applied projection/inbox atomically. Recovery reevaluates this area after the dependency returns.
                return;
            }
            verified = source.TotalTasks == tasks.Count && source.EligibleTasks == eligible &&
                source.CompletedTasks == completed && source.CancelledTasks == tasks.Count - eligible;
        }
        var readiness = await db.Set<LifecycleReadinessProjection>().FindAsync([areaId], cancellationToken);
        var confirmed = readiness is { Reopened: false, Invalidated: false };
        area.ReconcileExecution(eligible, completed, started, verified, confirmed,
            readiness is { Reopened: true }, clock.GetUtcNow());
        if (readiness is not null && area.Status == WorkAreaStatus.Ready) readiness.Applied = true;
    }

    private static void ValidateStatus(string status)
    {
        if (!Statuses.Contains(status)) throw new FormatException("Unsupported Task lifecycle status.");
    }

    private static Guid ReadGuid(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(property, out var value) &&
        value.TryGetGuid(out var id) && id != Guid.Empty ? id : throw new FormatException($"Missing lifecycle identifier '{property}'.");
    private static string ReadString(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new FormatException($"Missing lifecycle field '{property}'.");
    private static DateTimeOffset ReadTimestamp(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.TryGetDateTimeOffset(out var timestamp) && timestamp != default
            ? timestamp.ToUniversalTime() : throw new FormatException($"Invalid lifecycle timestamp '{property}'.");
}
