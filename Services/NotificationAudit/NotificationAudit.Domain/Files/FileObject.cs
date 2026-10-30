namespace NotificationAudit.Domain.Files;

public enum FilePurpose
{
    ProgressEvidence,
    ReadinessEvidence,
    PurchaseReceipt,
    Other
}

public enum FileObjectStatus
{
    PendingUpload,
    Uploaded,
    Attached,
    Deleted
}

public sealed class FileObject
{
    private FileObject() { }

    public Guid Id { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string? Checksum { get; private set; }
    public FilePurpose Purpose { get; private set; }
    public FileObjectStatus Status { get; private set; }
    public string? ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static FileObject CreateUploadRequest(
        Guid uploadedByUserId,
        string fileName,
        string contentType,
        long sizeBytes,
        string? checksum,
        FilePurpose purpose,
        DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        return new FileObject
        {
            Id = id,
            UploadedByUserId = uploadedByUserId,
            ObjectKey = $"pending/{uploadedByUserId:N}/{id:N}",
            OriginalFileName = fileName.Trim(),
            ContentType = contentType.Trim().ToLowerInvariant(),
            SizeBytes = sizeBytes,
            Checksum = string.IsNullOrWhiteSpace(checksum) ? null : checksum.Trim().ToLowerInvariant(),
            Purpose = purpose,
            Status = FileObjectStatus.PendingUpload,
            CreatedAt = now
        };
    }

    public void Complete(string completedObjectKey, string checksum, DateTimeOffset completedAt)
    {
        if (Status != FileObjectStatus.PendingUpload)
        {
            throw new InvalidOperationException("Only a pending upload can be completed.");
        }

        ObjectKey = completedObjectKey;
        Checksum = checksum;
        Status = FileObjectStatus.Uploaded;
        CompletedAt = completedAt;
    }
}
