using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using NotificationAudit.Contracts.SystemJobs;
using NotificationAudit.Domain.SystemJobs;

namespace NotificationAudit.Application.SystemJobs;

public static class SystemServiceCatalog
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    { "Identity", "ParishCoordination", "TaskExecution", "Resource", "Readiness", "Reporting", "NotificationAudit" });

    public static string[] Normalize(IReadOnlyList<string>? services)
    {
        if (services is null) return All.Order(StringComparer.Ordinal).ToArray();
        if (services.Count == 0 || services.Count > All.Count)
            throw new DomainException("SYSTEM_SERVICES_INVALID", "Choose one or more known services.", 422);
        var normalized = new List<string>();
        foreach (var value in services)
        {
            var name = All.SingleOrDefault(item => string.Equals(item, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null || normalized.Contains(name))
                throw new DomainException("SYSTEM_SERVICES_INVALID", "Services must be known and unique.", 422);
            normalized.Add(name);
        }
        return normalized.Order(StringComparer.Ordinal).ToArray();
    }
}

public interface ISystemJobStore
{
    // Atomic create/replay, serialized by owner/kind/key; keys are retained for the lifetime of the job.
    Task<SystemJob> CreateOrReplayAsync(SystemJob proposed, CancellationToken cancellationToken);
    Task<SystemJob?> FindAsync(Guid jobId, Guid owner, SystemJobKind kind, CancellationToken cancellationToken);
}

public interface ISystemArtifactAccess
{
    Task<DateTimeOffset?> GetExpiryAsync(Guid fileId, Guid owner, CancellationToken cancellationToken);
}

public sealed class SystemJobHandler(ICurrentUser currentUser, ISystemJobStore store, TimeProvider clock)
{
    public async Task<SystemJobDto> CreateAsync(SystemJobKind kind, IReadOnlyList<string>? services,
        string? idempotencyKey, Guid? correlationId, CancellationToken cancellationToken)
    {
        RequireAdmin();
        if (!Guid.TryParseExact(idempotencyKey, "D", out var key) || key == Guid.Empty)
            throw new DomainException("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key must be a non-empty UUID.", 400);
        var selected = SystemServiceCatalog.Normalize(services);
        var json = JsonSerializer.Serialize(selected);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}:{json}")));
        var proposed = SystemJob.Create(currentUser.UserId, kind, key, fingerprint, json, clock.GetUtcNow(), correlationId);
        return Map(await store.CreateOrReplayAsync(proposed, cancellationToken));
    }

    public async Task<SystemJobDto> GetBackupAsync(Guid jobId, CancellationToken cancellationToken)
    {
        RequireAdmin();
        var job = await store.FindAsync(jobId, currentUser.UserId, SystemJobKind.Backup, cancellationToken);
        if (job is null) throw new DomainException("BACKUP_NOT_FOUND", "The backup job does not exist.", 404);
        return Map(job);
    }

    private void RequireAdmin()
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != GrottoWorksRoles.Admin)
            throw new DomainException("FORBIDDEN", "Administrator access is required.", 403);
    }

    public static SystemJobDto Map(SystemJob job) => new(job.Id, job.Status.ToString().ToUpperInvariant(),
        JsonSerializer.Deserialize<string[]>(job.ServicesJson)!, job.CreatedAt, job.StartedAt, job.FinishedAt,
        job.Attempts, job.ArtifactFileId, job.ArtifactExpiresAt, job.ErrorCode,
        job.ManifestJson is null ? null : JsonSerializer.Deserialize<JsonElement>(job.ManifestJson));
}
