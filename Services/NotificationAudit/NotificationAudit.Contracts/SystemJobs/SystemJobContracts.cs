using System.Text.Json;

namespace NotificationAudit.Contracts.SystemJobs;

public sealed record SystemExportRequest(IReadOnlyList<string>? Services = null);
public sealed record BackupRequest(IReadOnlyList<string>? Services = null);
public sealed record SystemJobDto(Guid JobId, string Status, IReadOnlyList<string> Services,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
    int Attempts, Guid? FileId, DateTimeOffset? ExpiresAt, string? ErrorCode, JsonElement? Manifest);
