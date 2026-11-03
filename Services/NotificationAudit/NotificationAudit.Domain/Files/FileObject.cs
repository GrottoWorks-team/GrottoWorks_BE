namespace NotificationAudit.Domain.Files;

public enum FilePurpose
{
    ProgressEvidence,
    ReadinessEvidence,
    PurchaseReceipt,
    Other,
    SystemExport,
    DatabaseBackup
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
    public Guid? AttachmentReservationId { get; private set; }
    public string? AttachmentService { get; private set; }
    public string? ReservedReferenceType { get; private set; }
    public Guid? ReservedReferenceId { get; private set; }

    public bool ReserveAttachment(Guid reservationId, string service, string referenceType, Guid referenceId)
    {
        if (reservationId == Guid.Empty || referenceId == Guid.Empty || string.IsNullOrWhiteSpace(service) ||
            service.Length > 50 || string.IsNullOrWhiteSpace(referenceType) || referenceType.Length > 50)
            throw new ArgumentException("Attachment identity and reference are required.");
        if (AttachmentReservationId.HasValue)
        {
            EnsureReservation(reservationId, service, referenceType, referenceId);
            if (Status is not (FileObjectStatus.Uploaded or FileObjectStatus.Attached))
                throw new InvalidOperationException("The reserved file is not available.");
            return false;
        }
        if (Status != FileObjectStatus.Uploaded)
            throw new InvalidOperationException("Only a completed upload can be reserved.");
        AttachmentReservationId = reservationId;
        AttachmentService = service;
        ReservedReferenceType = referenceType;
        ReservedReferenceId = referenceId;
        return true;
    }

    public bool FinalizeAttachment(Guid reservationId, string service, string referenceType, Guid referenceId)
    {
        EnsureReservation(reservationId, service, referenceType, referenceId);
        if (Status == FileObjectStatus.Attached) return false;
        if (Status != FileObjectStatus.Uploaded)
            throw new InvalidOperationException("The reserved file is not available.");
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        Status = FileObjectStatus.Attached;
        return true;
    }

    public bool ReleaseAttachment(Guid reservationId, string service, string referenceType, Guid referenceId)
    {
        if (!AttachmentReservationId.HasValue) return false;
        EnsureReservation(reservationId, service, referenceType, referenceId);
        if (Status == FileObjectStatus.Attached)
            throw new InvalidOperationException("A finalized attachment cannot be released as a reservation.");
        AttachmentReservationId = null;
        AttachmentService = null;
        ReservedReferenceType = null;
        ReservedReferenceId = null;
        return true;
    }

    private void EnsureReservation(Guid id, string service, string referenceType, Guid referenceId)
    {
        if (!AttachmentReservationId.HasValue || AttachmentReservationId != id || AttachmentService != service ||
            ReservedReferenceType != referenceType || ReservedReferenceId != referenceId)
            throw new InvalidOperationException("The attachment reservation belongs to another operation or record.");
    }

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

    public static FileObject CreateSystemArtifact(Guid owner, Guid jobId, string key, string name,
        string contentType, long size, string checksum, bool isBackup, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), UploadedByUserId = owner, ObjectKey = key, OriginalFileName = name,
        ContentType = contentType, SizeBytes = size, Checksum = checksum,
        Purpose = isBackup ? FilePurpose.DatabaseBackup : FilePurpose.SystemExport,
        Status = FileObjectStatus.Attached, ReferenceType = "SYSTEM_JOB", ReferenceId = jobId,
        CreatedAt = now, CompletedAt = now
    };

    public void ExpireSystemArtifact()
    {
        if (Purpose is not (FilePurpose.SystemExport or FilePurpose.DatabaseBackup) || ReferenceType != "SYSTEM_JOB")
            throw new InvalidOperationException("Only a system artifact may expire through job retention.");
        Status = FileObjectStatus.Deleted;
    }

    public void MarkDeleted()
    {
        if (Status == FileObjectStatus.Attached || AttachmentReservationId.HasValue)
        {
            throw new InvalidOperationException("An attached file cannot be deleted.");
        }

        Status = FileObjectStatus.Deleted;
    }
}
