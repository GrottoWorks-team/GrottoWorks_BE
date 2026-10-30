using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using NotificationAudit.Contracts.Files;
using NotificationAudit.Domain.Files;

namespace NotificationAudit.Application.Files;

public interface IFileObjectRepository
{
    void Add(FileObject file);
    Task<IFileObjectTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<FileObject?> GetForUpdateAsync(Guid fileId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IFileObjectTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IFileObjectStorage
{
    Task<PresignedObjectRequest> CreateUploadRequestAsync(
        string objectKey,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    Task<StoredObjectInspection> InspectAsync(string objectKey, CancellationToken cancellationToken);
    Task CommitUploadAsync(string pendingObjectKey, string completedObjectKey, CancellationToken cancellationToken);
}

public sealed record PresignedObjectRequest(
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt);

public sealed record StoredObjectInspection(
    long SizeBytes,
    string? ContentType,
    string Sha256,
    string DetectedContentType);

public sealed class FileStorageUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class FileUploadOptions
{
    public const string SectionName = "FileUpload";
    public int UploadUrlMinutes { get; init; } = 10;
    public bool AllowOtherPdf { get; init; }
}

public sealed class CompleteUploadHandler(
    ICurrentUser currentUser,
    IFileObjectRepository repository,
    IFileObjectStorage storage,
    TimeProvider timeProvider)
{
    public async Task<FileReferenceDto> HandleAsync(
        Guid fileId,
        CompleteUploadRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var file = await repository.GetForUpdateAsync(fileId, cancellationToken);
        if (file is null || file.UploadedByUserId != currentUser.UserId)
        {
            throw new DomainException("FILE_NOT_FOUND", "The file does not exist.", 404);
        }

        if (file.Status == FileObjectStatus.Uploaded || file.Status == FileObjectStatus.Attached)
        {
            await transaction.CommitAsync(cancellationToken);
            return Map(file);
        }

        if (file.Status != FileObjectStatus.PendingUpload)
        {
            throw new DomainException("FILE_INVALID_STATE", "The file cannot be completed in its current state.", 409);
        }

        StoredObjectInspection inspection;
        try
        {
            inspection = await storage.InspectAsync(file.ObjectKey, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            throw new DomainException("FILE_STORAGE_UNAVAILABLE", "File storage is temporarily unavailable.", 503);
        }

        ValidateStoredObject(file, request.Checksum, inspection);
        var completedObjectKey = $"completed/{file.UploadedByUserId:N}/{file.Id:N}";

        try
        {
            await storage.CommitUploadAsync(file.ObjectKey, completedObjectKey, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            throw new DomainException("FILE_STORAGE_UNAVAILABLE", "File storage is temporarily unavailable.", 503);
        }

        file.Complete(completedObjectKey, inspection.Sha256, timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(file);
    }

    public static void ValidateStoredObject(
        FileObject file,
        string? requestedChecksum,
        StoredObjectInspection inspection)
    {
        if (inspection.SizeBytes != file.SizeBytes)
        {
            throw new DomainException("FILE_SIZE_MISMATCH", "Uploaded file size does not match the upload request.", 422);
        }

        if (!string.Equals(inspection.DetectedContentType, file.ContentType, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(inspection.ContentType) &&
             !string.Equals(inspection.ContentType, file.ContentType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("FILE_CONTENT_TYPE_MISMATCH", "Uploaded content does not match the declared type.", 422);
        }

        var expectedChecksum = string.IsNullOrWhiteSpace(file.Checksum)
            ? requestedChecksum?.Trim().ToLowerInvariant()
            : file.Checksum;
        if (!string.IsNullOrWhiteSpace(requestedChecksum) &&
            !string.IsNullOrWhiteSpace(file.Checksum) &&
            !string.Equals(requestedChecksum.Trim(), file.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("FILE_CHECKSUM_MISMATCH", "Checksum does not match the upload request.", 422);
        }

        if (!string.IsNullOrWhiteSpace(expectedChecksum) &&
            !string.Equals(expectedChecksum, inspection.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("FILE_CHECKSUM_MISMATCH", "Uploaded file checksum is invalid.", 422);
        }
    }

    private static FileReferenceDto Map(FileObject file) =>
        new(file.Id, file.OriginalFileName, file.ContentType, file.SizeBytes);
}

public sealed class CreateUploadRequestHandler(
    ICurrentUser currentUser,
    IFileObjectRepository repository,
    IFileObjectStorage storage,
    Microsoft.Extensions.Options.IOptions<FileUploadOptions> options,
    TimeProvider timeProvider)
{
    public async Task<UploadRequestDto> HandleAsync(
        CreateUploadRequest request,
        CancellationToken cancellationToken)
    {
        var contentType = request.ContentType.Trim().ToLowerInvariant();
        ValidateFile(request.Purpose, contentType, request.SizeBytes, options.Value.AllowOtherPdf);

        var now = timeProvider.GetUtcNow();
        var file = FileObject.CreateUploadRequest(
            currentUser.UserId,
            request.FileName,
            contentType,
            request.SizeBytes,
            request.Checksum,
            MapPurpose(request.Purpose),
            now);

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(options.Value.UploadUrlMinutes, 1, 60));
        var presigned = await storage.CreateUploadRequestAsync(
            file.ObjectKey,
            file.ContentType,
            lifetime,
            cancellationToken);

        repository.Add(file);
        await repository.SaveChangesAsync(cancellationToken);

        return new UploadRequestDto(file.Id, presigned.Url, "PUT", presigned.Headers, presigned.ExpiresAt);
    }

    public static void ValidateFile(
        FilePurposeDto purpose,
        string contentType,
        long sizeBytes,
        bool allowOtherPdf)
    {
        var imageTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "image/webp"
        };

        var valid = purpose switch
        {
            FilePurposeDto.ProgressEvidence or FilePurposeDto.ReadinessEvidence =>
                imageTypes.Contains(contentType) && sizeBytes <= 10L * 1024 * 1024,
            FilePurposeDto.PurchaseReceipt =>
                (imageTypes.Contains(contentType) || contentType == "application/pdf") &&
                sizeBytes <= 15L * 1024 * 1024,
            FilePurposeDto.Other =>
                allowOtherPdf && contentType == "application/pdf" && sizeBytes <= 20L * 1024 * 1024,
            _ => false
        };

        if (!valid)
        {
            throw new DomainException(
                "FILE_UPLOAD_NOT_ALLOWED",
                "The file type or size is not allowed for the selected purpose.",
                422);
        }
    }

    private static FilePurpose MapPurpose(FilePurposeDto purpose) => purpose switch
    {
        FilePurposeDto.ProgressEvidence => FilePurpose.ProgressEvidence,
        FilePurposeDto.ReadinessEvidence => FilePurpose.ReadinessEvidence,
        FilePurposeDto.PurchaseReceipt => FilePurpose.PurchaseReceipt,
        FilePurposeDto.Other => FilePurpose.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };
}
