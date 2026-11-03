using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Readiness.Application.Checklists;
using Readiness.Domain;
using Readiness.Domain.Checklists;
using Readiness.Domain.Scope;
using Readiness.Infrastructure.Data;

namespace Readiness.Infrastructure.Checklists;

public sealed class DurableCorrectiveTaskCreator(DbContextOptions<ReadinessDbContext> options, TaskCorrectiveClient remote,
    ILogger<DurableCorrectiveTaskCreator> logger) : ICorrectiveTaskCreator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CreatedCorrectiveTask> CreateAsync(CorrectiveTaskCreationRequest request, CancellationToken ct)
    {
        request.Task.Validate();
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        var fingerprint = CorrectiveTaskIntent.Hash(JsonSerializer.Serialize(request with { PreviousTaskId = null }, JsonOptions));
        await using var journal = new ReadinessDbContext(options);
        await journal.Database.OpenConnectionAsync(ct);
        await journal.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({request.Task.RequestId.ToString()}, 713));", ct);
        try
        {
            var intent = await journal.Set<CorrectiveTaskIntent>().FindAsync([request.Task.RequestId], ct);
            if (intent is null)
            {
                intent = new CorrectiveTaskIntent { Id = request.Task.RequestId, CheckId = request.ReadinessCheckId,
                    ItemId = request.ReadinessItemId, WorkAreaId = request.WorkAreaId, RequestedByUserId = request.RequestedByUserId,
                    Payload = payload, Fingerprint = fingerprint, CreatedAt = DateTimeOffset.UtcNow, NextAttemptAt = DateTimeOffset.UtcNow };
                journal.Add(intent);
                await journal.SaveChangesAsync(ct); // This durable insert precedes the first HTTP request.
            }
            else
            {
                intent.EnsureSameRequest(fingerprint);
                request = JsonSerializer.Deserialize<CorrectiveTaskCreationRequest>(intent.Payload, JsonOptions)
                    ?? throw new InvalidOperationException("Invalid durable corrective request.");
            }
            if (intent.State == "SUPERSEDED") throw new DomainRuleException("CORRECTIVE_REQUEST_SUPERSEDED", "The readiness inspection changed before this request could finish.");
            return await SendAsync(journal, intent, request, ct);
        }
        finally { await journal.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({request.Task.RequestId.ToString()}, 713));", CancellationToken.None); }
    }

    private async Task<CreatedCorrectiveTask> SendAsync(ReadinessDbContext journal, CorrectiveTaskIntent intent, CorrectiveTaskCreationRequest request, CancellationToken ct)
    {
        // Replay even when TaskId is known: Task returns its original durable idempotent result, with ownership validation.
        var task = await remote.CreateAsync(request, ct);
        if (intent.TaskId is { } previous && previous != task.Id)
            throw new DomainRuleException("CORRECTIVE_TASK_ID_CHANGED", "Task service changed the idempotent result.", DomainErrorKind.ServiceUnavailable);
        intent.TaskId = task.Id;
        await journal.SaveChangesAsync(ct);
        return task;
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        await using var listing = new ReadinessDbContext(options);
        var ids = await listing.Set<CorrectiveTaskIntent>().AsNoTracking()
            .Where(x => x.State == "PENDING" && x.NextAttemptAt <= DateTimeOffset.UtcNow)
            .OrderBy(x => x.NextAttemptAt).Take(25).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var journal = new ReadinessDbContext(options);
            await journal.Database.OpenConnectionAsync(ct);
            await journal.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({id.ToString()}, 713));", ct);
            try
            {
                var intent = await journal.Set<CorrectiveTaskIntent>().FindAsync([id], ct);
                if (intent is null || intent.State != "PENDING" || intent.NextAttemptAt > DateTimeOffset.UtcNow) continue;
                try
                {
                    var request = JsonSerializer.Deserialize<CorrectiveTaskCreationRequest>(intent.Payload, JsonOptions)
                        ?? throw new InvalidOperationException("Invalid durable corrective request.");
                    var check = await journal.Checklists.Include(x => x.Items).FirstOrDefaultAsync(x => x.ReadinessChecklistId == intent.CheckId, ct);
                    var item = check?.Items.FirstOrDefault(x => x.ReadinessItemId == intent.ItemId);
                    if (item is not null && intent.TaskId is { } linked && item.CorrectiveTaskId == linked)
                    { intent.State = "LINKED"; await journal.SaveChangesAsync(ct); continue; }
                    if (check is null || item is null || check.Status == ChecklistStatus.Ready ||
                        item.CheckedAt != request.InspectionAt || item.CorrectiveTaskId != request.PreviousTaskId ||
                        item.Status is not (ItemStatus.Failed or ItemStatus.CorrectiveAction))
                    { intent.State = "SUPERSEDED"; await journal.SaveChangesAsync(ct); continue; }
                    var scope = await journal.WorkAreaScopes.AsNoTracking().FirstOrDefaultAsync(x => x.WorkAreaId == intent.WorkAreaId, ct);
                    if (scope is null || scope.IsReadOnly || scope.LeaderUserId != intent.RequestedByUserId)
                        throw new InvalidOperationException("Corrective request scope is unavailable or no longer writable by its leader.");
                    var task = await SendAsync(journal, intent, request, ct);
                    var projection = await journal.TaskProjections.AsNoTracking().FirstOrDefaultAsync(x => x.TaskId == task.Id, ct);
                    check.LinkCorrectiveTask(item.ReadinessItemId, task.Id, task.Status == "COMPLETED" || projection?.Status == ProjectedTaskStatus.Completed);
                    intent.State = "LINKED";
                    await journal.SaveChangesAsync(ct); // Link and completion of the intent commit together.
                }
                catch (Exception exception) when (!ct.IsCancellationRequested)
                {
                    journal.ChangeTracker.Clear();
                    var retry = await journal.Set<CorrectiveTaskIntent>().FindAsync([id], ct);
                    if (retry is null) continue;
                    retry.Attempts++;
                    retry.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(retry.Attempts, 6))));
                    await journal.SaveChangesAsync(ct);
                    logger.LogWarning(exception, "Corrective task {RequestId} remains pending; attempt {Attempt}", id, retry.Attempts);
                }
            }
            finally { await journal.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({id.ToString()}, 713));", CancellationToken.None); }
        }
    }
}
