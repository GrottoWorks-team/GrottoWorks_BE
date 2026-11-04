using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Resource.Application.Files;
using Resource.Domain.Files;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Files;

public sealed class AttachmentCoordinator(ResourceDbContext business, DbContextOptions<ResourceDbContext> dbOptions,
    FileAttachmentClient remote, ILogger<AttachmentCoordinator> logger) : IFileVerifier, IRecordFileDownload
{
    public Task EnsureAttachableAsync(IReadOnlyCollection<Guid> fileIds, string purpose, Guid uploaderUserId, CancellationToken cancellationToken = default) =>
        remote.EnsureAttachableAsync(fileIds, purpose, uploaderUserId, cancellationToken);

    public async Task<IAsyncDisposable> ReserveAsync(IReadOnlyCollection<Guid> fileIds, string purpose, Guid uploaderUserId,
        Guid referenceId, CancellationToken cancellationToken = default)
    {
        if (fileIds.Any(x => x == Guid.Empty) || referenceId == Guid.Empty || uploaderUserId == Guid.Empty)
            throw Resource.Domain.DomainRuleException.Validation("FILE_REFERENCE_INVALID", "Valid file, record and uploader IDs are required.");
        var lease = await LockAsync(referenceId, cancellationToken);
        try
        {
            foreach (var fileId in fileIds.Distinct())
            {
                var id = AttachmentIntent.Identity(fileId, referenceId);
                var intent = await lease.Journal.Set<AttachmentIntent>().FindAsync([id], cancellationToken);
                if (intent is null)
                {
                    intent = new AttachmentIntent { Id = id, FileId = fileId, ReferenceId = referenceId,
                        UploadedByUserId = uploaderUserId, Purpose = purpose, CreatedAt = DateTimeOffset.UtcNow,
                        NextAttemptAt = DateTimeOffset.UtcNow };
                    lease.Journal.Add(intent);
                }
                else if (intent.UploadedByUserId != uploaderUserId || intent.Purpose != purpose)
                    throw Resource.Domain.DomainRuleException.Validation("FILE_NOT_OWNED", "The file reservation belongs to a different uploader or purpose.");
                intent.State = "RESERVING";
                await lease.Journal.SaveChangesAsync(cancellationToken); // Commit before any HTTP command, including a possible timeout.
                await remote.CommandAsync(intent, "", cancellationToken);
                var pending = await business.Set<AttachmentIntent>().FindAsync([id], cancellationToken);
                if (pending is null) throw new InvalidOperationException("Durable attachment intent disappeared.");
                pending.State = "FINALIZE"; // Business record and finalize instruction commit in the same SaveChanges.
                pending.NextAttemptAt = DateTimeOffset.UtcNow;
            }
            return lease;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    public async Task<RecordFileDownload> GetAsync(Guid fileId, Guid referenceId, CancellationToken cancellationToken)
    {
        await using var lease = await LockAsync(referenceId, cancellationToken, recovery: true);
        if (!await ContainsAsync(lease.Journal, referenceId, fileId, cancellationToken))
            throw Resource.Domain.DomainRuleException.NotFound("FILE_NOT_FOUND", "The file does not belong to this record.");
        var intent = await lease.Journal.Set<AttachmentIntent>().FindAsync([AttachmentIntent.Identity(fileId, referenceId)], cancellationToken);
        if (intent is null) throw new Resource.Domain.DomainRuleException("FILE_ATTACHMENT_PENDING", "This legacy attachment requires reconciliation.", Resource.Domain.DomainErrorKind.ServiceUnavailable);
        await ReconcileAsync(lease.Journal, intent, cancellationToken);
        return await remote.DownloadAsync(fileId, referenceId, cancellationToken);
    }

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var journal = new ResourceDbContext(dbOptions);
        var pending = await journal.Set<AttachmentIntent>().AsNoTracking()
            .Where(x => x.State != "FINALIZED" && x.State != "RELEASED" && x.NextAttemptAt <= DateTimeOffset.UtcNow)
            .OrderBy(x => x.NextAttemptAt).Take(50).Select(x => new { x.Id, x.ReferenceId }).ToListAsync(cancellationToken);
        foreach (var row in pending)
        {
            try
            {
                await using var lease = await LockAsync(row.ReferenceId, cancellationToken, recovery: true);
                var intent = await lease.Journal.Set<AttachmentIntent>().FindAsync([row.Id], cancellationToken);
                if (intent is null || intent.State is "FINALIZED" or "RELEASED" || intent.NextAttemptAt > DateTimeOffset.UtcNow) continue;
                try { await ReconcileAsync(lease.Journal, intent, cancellationToken); }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    intent.Attempts++;
                    intent.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(intent.Attempts, 6))));
                    await lease.Journal.SaveChangesAsync(cancellationToken);
                    logger.LogWarning(exception, "Attachment reconciliation failed for {ReservationId}; retry {Attempt}", intent.Id, intent.Attempts);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            { logger.LogWarning(exception, "Attachment recovery could not acquire record {ReferenceId}", row.ReferenceId); }
        }
    }

    private async Task ReconcileAsync(ResourceDbContext journal, AttachmentIntent intent, CancellationToken ct)
    {
        var action = intent.RecoveryAction(await ContainsAsync(journal, intent.ReferenceId, intent.FileId, ct));
        if (action == "FINALIZE")
        {
            await remote.CommandAsync(intent, "", ct); // A crashed reserve response can be replayed safely.
            await remote.CommandAsync(intent, "/finalize", ct);
            intent.State = "FINALIZED";
        }
        else
        {
            await remote.CommandAsync(intent, "/release", ct);
            intent.State = "RELEASED";
        }
        await journal.SaveChangesAsync(ct);
    }

    private static async Task<bool> ContainsAsync(ResourceDbContext journal, Guid referenceId, Guid fileId, CancellationToken ct)
    {
        return await journal.Purchases.AsNoTracking().AnyAsync(x => x.PurchaseId == referenceId && x.ReceiptFileIds.Contains(fileId), ct);
    }

    private async Task<RecordLease> LockAsync(Guid referenceId, CancellationToken ct, bool recovery = false)
    {
        var journal = new ResourceDbContext(dbOptions);
        try
        {
            await journal.Database.OpenConnectionAsync(ct);
            // Session lock spans the independently committed journal and the business SaveChanges.
            // Reconciliation takes exactly the same lock. Never infer rollback from elapsed time.
            await journal.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({referenceId.ToString()}, 711));", ct);
            if (recovery)
                await journal.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({referenceId.ToString()}, 712));", ct);
            return new RecordLease(journal, referenceId, recovery);
        }
        catch { await journal.DisposeAsync(); throw; }
    }

    private sealed class RecordLease(ResourceDbContext journal, Guid referenceId, bool recovery) : IAsyncDisposable
    {
        public ResourceDbContext Journal { get; } = journal;
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (recovery)
                    await Journal.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({referenceId.ToString()}, 712));", CancellationToken.None);
                await Journal.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({referenceId.ToString()}, 711));", CancellationToken.None);
            }
            finally { await Journal.DisposeAsync(); }
        }
    }
}
