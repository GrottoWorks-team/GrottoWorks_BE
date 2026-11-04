using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Domain.Files;
using NotificationAudit.Domain.Notifications;
using NotificationAudit.Domain.SystemJobs;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.SystemJobs;

public sealed class SystemJobExecutionStore(NotificationAuditDbContext db, TimeProvider clock,
    IOptions<SystemJobOptions> configuredOptions) : ISystemJobExecutionStore
{
    private readonly SystemJobOptions options = configuredOptions.Value;

    public async Task<SystemJob?> ClaimAsync(SystemJobKind kind, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var name = kind.ToString();
        var job = (await db.Set<SystemJob>().FromSqlInterpolated($"""
            SELECT * FROM system_job
            WHERE kind = {name} AND next_attempt_at <= CURRENT_TIMESTAMP
              AND (status = 'Queued' OR (status = 'Running' AND lease_until < CURRENT_TIMESTAMP))
            ORDER BY next_attempt_at, created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken)).SingleOrDefault();
        if (job is null) return null;
        // Exhausted crash recovery is terminal too; otherwise a crashing worker could retry forever.
        job.Claim(Guid.NewGuid(), clock.GetUtcNow(), TimeSpan.FromMinutes(5));
        if (job.Attempts > options.MaximumAttempts)
        {
            job.Fail(job.LeaseToken!.Value, "SYSTEM_JOB_RETRY_EXHAUSTED",
                job.ManifestJson ?? "{\"complete\":false}", clock.GetUtcNow(), options.MaximumAttempts);
            AddFailureNotification(job);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job.Status == SystemJobStatus.Running ? job : null;
    }

    public async Task<bool> RenewAsync(Guid id, Guid token, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE system_job SET lease_until = CURRENT_TIMESTAMP + INTERVAL '5 minutes'
            WHERE id = {id} AND lease_token = {token} AND status = 'Running' AND lease_until > CURRENT_TIMESTAMP
            """, cancellationToken) == 1;

    public async Task<bool> CompleteAsync(Guid id, Guid token, string key, GeneratedSystemArtifact artifact,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await LockedAsync(id, token, cancellationToken);
        if (job is null) return false;
        var now = clock.GetUtcNow();
        var file = FileObject.CreateSystemArtifact(job.OwnerUserId, job.Id, key,
            $"{job.Kind.ToString().ToLowerInvariant()}-{job.Id:N}{artifact.Extension}", artifact.ContentType,
            artifact.Length, artifact.Sha256, job.Kind == SystemJobKind.Backup, now);
        job.Complete(token, file.Id, key, artifact.ManifestJson, now, now.AddDays(options.RetentionDays));
        db.FileObjects.Add(file);
        db.Notifications.Add(Notification.Create(job.OwnerUserId,
            job.Kind == SystemJobKind.Export ? "system.export.completed" : "system.backup.completed",
            job.Kind == SystemJobKind.Export ? "System export ready" : "Encrypted backup ready",
            "The artifact is ready for download through its file reference.", "FILE", file.Id, now));
        AddAudit(job, "completed", now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task FailAsync(Guid id, Guid token, string code, string manifest, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await LockedAsync(id, token, cancellationToken);
        if (job is null) return;
        job.Fail(token, code, manifest, clock.GetUtcNow(), options.MaximumAttempts);
        if (job.Status == SystemJobStatus.Failed)
        {
            AddFailureNotification(job);
            AddAudit(job, "failed", clock.GetUtcNow());
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<SystemJob?> LockedAsync(Guid id, Guid token, CancellationToken ct) =>
        (await db.Set<SystemJob>().FromSqlInterpolated($"""
            SELECT * FROM system_job WHERE id = {id} AND lease_token = {token}
              AND status = 'Running' AND lease_until > CURRENT_TIMESTAMP FOR UPDATE
            """).ToListAsync(ct)).SingleOrDefault();

    private void AddFailureNotification(SystemJob job) => db.Notifications.Add(Notification.Create(job.OwnerUserId,
        job.Kind == SystemJobKind.Export ? "system.export.failed" : "system.backup.failed",
        "System job failed", "The job could not capture every required source. Review its manifest and error code.",
        "SYSTEM_JOB", job.Id, clock.GetUtcNow()));

    private void AddAudit(SystemJob job, string result, DateTimeOffset now) =>
        db.ActivityLogs.Add(NotificationAudit.Domain.Audit.ActivityLog.Create(job.OwnerUserId, null, null,
            $"system.{job.Kind.ToString().ToLowerInvariant()}.{result}", "SYSTEM_JOB", job.Id,
            null, null, null, job.CorrelationId, now));
}
