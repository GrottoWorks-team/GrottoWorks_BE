using BuildingBlocks.Web.Exceptions;
using Microsoft.EntityFrameworkCore;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Domain.SystemJobs;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.SystemJobs;

public sealed class SystemJobStore(NotificationAuditDbContext db, TimeProvider clock) : ISystemJobStore, ISystemArtifactAccess
{
    public async Task<SystemJob> CreateOrReplayAsync(SystemJob proposed, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var identity = $"{proposed.OwnerUserId:D}:{proposed.Kind}:{proposed.IdempotencyKey:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0))", cancellationToken);
        var existing = await db.Set<SystemJob>().SingleOrDefaultAsync(job => job.OwnerUserId == proposed.OwnerUserId &&
            job.Kind == proposed.Kind && job.IdempotencyKey == proposed.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Fingerprint != proposed.Fingerprint)
                throw new DomainException("IDEMPOTENCY_CONFLICT", "The key was already used with another request.", 409);
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }
        db.Add(proposed);
        db.ActivityLogs.Add(NotificationAudit.Domain.Audit.ActivityLog.Create(proposed.OwnerUserId, null, null,
            proposed.Kind == SystemJobKind.Export ? "system.export.requested" : "system.backup.requested",
            "SYSTEM_JOB", proposed.Id, null, null, null, proposed.CorrelationId, proposed.CreatedAt));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return proposed;
    }

    public Task<SystemJob?> FindAsync(Guid jobId, Guid owner, SystemJobKind kind, CancellationToken cancellationToken) =>
        db.Set<SystemJob>().AsNoTracking().SingleOrDefaultAsync(job => job.Id == jobId && job.OwnerUserId == owner && job.Kind == kind, cancellationToken);

    public Task<DateTimeOffset?> GetExpiryAsync(Guid fileId, Guid owner, CancellationToken cancellationToken) =>
        db.Set<SystemJob>().AsNoTracking().Where(job => job.ArtifactFileId == fileId && job.OwnerUserId == owner &&
            job.Status == SystemJobStatus.Completed && job.ArtifactExpiresAt > clock.GetUtcNow())
            .Select(job => job.ArtifactExpiresAt).SingleOrDefaultAsync(cancellationToken);
}
