using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using NotificationAudit.Domain.Files;
using NotificationAudit.Domain.SystemJobs;
using NotificationAudit.Infrastructure.Data;
using NotificationAudit.Infrastructure.Files;

namespace NotificationAudit.Infrastructure.SystemJobs;

/// <summary>Delayed, reconciled deletion. Never infer an orphan from an upload/DB timeout alone.</summary>
public sealed class SystemObjectCleanupWorker(IServiceScopeFactory scopes, IOptions<ObjectStorageOptions> configuredOptions,
    TimeProvider clock, ILogger<SystemObjectCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Object cleanup could not complete; the next sweep will retry."); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        var options = configuredOptions.Value;
        using var client = MinioFileObjectStorage.CreateClient(options.InternalEndpoint, options.AccessKey, options.SecretKey);
        foreach (var prefix in new[] { "system-jobs/", "completed/", "pending/" })
        {
            await foreach (var item in client.ListObjectsEnumAsync(new ListObjectsArgs().WithBucket(options.Bucket).WithPrefix(prefix).WithRecursive(true), ct))
            {
                ct.ThrowIfCancellationRequested();
                if (item.IsDir || !DateTimeOffset.TryParse(item.LastModified, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var modified) || modified > clock.GetUtcNow().AddDays(-1)) continue;
                if (!await IsRemovableAsync(item.Key, ct)) continue;
                await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(options.Bucket).WithObject(item.Key), ct);
            }
        }
    }

    private async Task<bool> IsRemovableAsync(string key, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationAuditDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var parts = key.Split('/');
        var now = clock.GetUtcNow();
        if (parts.Length == 3 && parts[0] == "system-jobs" && Guid.TryParseExact(parts[1], "N", out var jobId))
        {
            var tokenText = parts[2].Split('.')[0];
            if (!Guid.TryParseExact(tokenText, "N", out var token)) return false;
            var job = (await db.Set<SystemJob>().FromSqlInterpolated($"SELECT * FROM system_job WHERE id = {jobId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
            if (job?.ArtifactObjectKey == key)
            {
                if (job.ArtifactExpiresAt is null || job.ArtifactExpiresAt > now) return false;
                var file = await db.FileObjects.SingleOrDefaultAsync(file => file.Id == job.ArtifactFileId, ct);
                file?.ExpireSystemArtifact();
                await db.SaveChangesAsync(ct);
            }
            else if (job?.LeaseToken == token && job.Status == SystemJobStatus.Running && job.LeaseUntil > now) return false;
            await transaction.CommitAsync(ct);
            return true;
        }
        if ((parts.Length == 3 && parts[0] == "pending" || parts.Length == 4 && parts[0] == "completed") &&
            Guid.TryParseExact(parts[2], "N", out var fileId))
        {
            var file = (await db.FileObjects.FromSqlInterpolated($"SELECT * FROM file_object WHERE file_id = {fileId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
            if (file?.ObjectKey == key && file.Status != FileObjectStatus.Deleted)
            {
                if (file.Status != FileObjectStatus.PendingUpload || file.CreatedAt > now.AddDays(-1) || file.AttachmentReservationId.HasValue) return false;
                file.MarkDeleted();
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return true;
        }
        return false;
    }
}
