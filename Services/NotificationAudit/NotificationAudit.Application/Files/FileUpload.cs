using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using NotificationAudit.Contracts.Files;
using NotificationAudit.Domain.Files;

namespace NotificationAudit.Application.Files;

public interface IFileObjectRepository
{
    void Add(FileObject file);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IFileObjectStorage
{
    Task<PresignedObjectRequest> CreateUploadRequestAsync(
        string objectKey,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken);
}

public sealed record PresignedObjectRequest(
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt);

public sealed class FileUploadOptions
{
    public const string SectionName = "FileUpload";
    public int UploadUrlMinutes { get; init; } = 10;
    public bool AllowOtherPdf { get; init; }
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
