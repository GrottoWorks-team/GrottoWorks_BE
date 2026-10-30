using System.ComponentModel.DataAnnotations;

namespace NotificationAudit.Contracts.Files;

public enum FilePurposeDto
{
    ProgressEvidence,
    ReadinessEvidence,
    PurchaseReceipt,
    Other
}

public sealed record CreateUploadRequest(
    [property: Required, StringLength(255, MinimumLength = 1)] string FileName,
    [property: Required, StringLength(100, MinimumLength = 1)] string ContentType,
    [property: Range(1, long.MaxValue)] long SizeBytes,
    FilePurposeDto Purpose,
    [property: RegularExpression("^[A-Fa-f0-9]{64}$")] string? Checksum);

public sealed record UploadRequestDto(
    Guid FileId,
    string UploadUrl,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt);

public sealed record CompleteUploadRequest(
    [property: RegularExpression("^[A-Fa-f0-9]{64}$")] string? Checksum);

public sealed record FileReferenceDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes);

public sealed record FileDownloadDto(string DownloadUrl, DateTimeOffset ExpiresAt);

public sealed record InternalFileDto(
    Guid Id,
    Guid UploadedByUserId,
    string Purpose,
    string Status,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Checksum,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
