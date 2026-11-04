namespace NotificationAudit.Domain.SystemJobs;

public enum SystemJobKind { Export, Backup }
public enum SystemJobStatus { Queued, Running, Completed, Failed }

/// <summary>A durable command. Each claim gets a new fencing token, including restart recovery.</summary>
public sealed class SystemJob
{
    private SystemJob() { }
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public SystemJobKind Kind { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string ServicesJson { get; private set; } = "[]";
    public SystemJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public int Attempts { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public Guid? ArtifactFileId { get; private set; }
    public string? ArtifactObjectKey { get; private set; }
    public DateTimeOffset? ArtifactExpiresAt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ManifestJson { get; private set; }
    public Guid? CorrelationId { get; private set; }

    public static SystemJob Create(Guid owner, SystemJobKind kind, Guid key, string fingerprint,
        string servicesJson, DateTimeOffset now, Guid? correlationId) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, Kind = kind, IdempotencyKey = key,
        Fingerprint = fingerprint, ServicesJson = servicesJson, CreatedAt = now,
        NextAttemptAt = now, Status = SystemJobStatus.Queued, CorrelationId = correlationId
    };

    public void Claim(Guid token, DateTimeOffset now, TimeSpan lifetime)
    {
        if (Status is SystemJobStatus.Completed or SystemJobStatus.Failed || NextAttemptAt > now ||
            (Status == SystemJobStatus.Running && LeaseUntil > now))
            throw new InvalidOperationException("The job is not available for claiming.");
        Status = SystemJobStatus.Running;
        StartedAt ??= now;
        Attempts++;
        LeaseToken = token;
        LeaseUntil = now.Add(lifetime);
    }

    public void Complete(Guid token, Guid fileId, string objectKey, string manifest,
        DateTimeOffset now, DateTimeOffset expiresAt)
    {
        RequireLease(token, now);
        Status = SystemJobStatus.Completed;
        ArtifactFileId = fileId;
        ArtifactObjectKey = objectKey;
        ArtifactExpiresAt = expiresAt;
        ManifestJson = manifest;
        ErrorCode = null;
        FinishedAt = now;
        LeaseToken = null;
        LeaseUntil = null;
    }

    public void Fail(Guid token, string code, string manifest, DateTimeOffset now, int maximumAttempts)
    {
        RequireLease(token, now);
        ErrorCode = code;
        ManifestJson = manifest;
        Status = Attempts >= maximumAttempts ? SystemJobStatus.Failed : SystemJobStatus.Queued;
        FinishedAt = Status == SystemJobStatus.Failed ? now : null;
        NextAttemptAt = now.AddSeconds(Math.Min(900, 15 * Math.Pow(2, Math.Min(Attempts, 6))));
        LeaseToken = null;
        LeaseUntil = null;
    }

    public void RequireLease(Guid token, DateTimeOffset now)
    {
        if (Status != SystemJobStatus.Running || LeaseToken != token || LeaseUntil is null || LeaseUntil <= now)
            throw new InvalidOperationException("The job lease was lost.");
    }
}
