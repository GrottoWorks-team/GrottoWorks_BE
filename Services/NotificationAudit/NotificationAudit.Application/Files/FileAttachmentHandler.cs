using BuildingBlocks.Web.Exceptions;
using Microsoft.Extensions.Options;
using NotificationAudit.Contracts.Files;
using NotificationAudit.Domain.Files;

namespace NotificationAudit.Application.Files;

public sealed class FileAttachmentHandler(IFileObjectRepository repository, IFileObjectStorage storage,
    IOptions<FileUploadOptions> options)
{
    public async Task<FileAttachmentDto> ReserveAsync(Guid fileId, ReserveFileAttachmentRequest request, CancellationToken cancellationToken)
    {
        Validate(request.ReservationId, request.Service, request.ReferenceType, request.ReferenceId);
        if (request.UploadedByUserId == Guid.Empty || !Enum.IsDefined(request.Purpose))
            throw new DomainException("VALIDATION_FAILED", "Uploader and valid purpose are required.", 422);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var file = await repository.GetForUpdateAsync(fileId, cancellationToken) ?? throw NotFound();
        if (file.UploadedByUserId != request.UploadedByUserId)
            throw new DomainException("FILE_ACCESS_DENIED", "The uploader does not match the business operation.", 403);
        if (file.Purpose.ToString() != request.Purpose.ToString())
            throw new DomainException("FILE_PURPOSE_MISMATCH", "The file purpose is not valid for this record.", 422);
        bool changed;
        try { changed = file.ReserveAttachment(request.ReservationId, request.Service, request.ReferenceType, request.ReferenceId); }
        catch (InvalidOperationException exception) { throw Conflict(exception); }
        if (changed) await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(file.Id, request.ReservationId, request.Service, request.ReferenceType, request.ReferenceId,
            file.Status == FileObjectStatus.Attached);
    }

    public async Task<FileReferenceDto> FinalizeAsync(Guid fileId, FileAttachmentCommand request, CancellationToken cancellationToken)
    {
        Validate(request.ReservationId, request.Service, request.ReferenceType, request.ReferenceId);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var file = await repository.GetForUpdateAsync(fileId, cancellationToken) ?? throw NotFound();
        bool changed;
        try { changed = file.FinalizeAttachment(request.ReservationId, request.Service, request.ReferenceType, request.ReferenceId); }
        catch (InvalidOperationException exception) { throw Conflict(exception); }
        if (changed) await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(file.Id, file.OriginalFileName, file.ContentType, file.SizeBytes);
    }

    public async Task ReleaseAsync(Guid fileId, FileAttachmentCommand request, CancellationToken cancellationToken)
    {
        Validate(request.ReservationId, request.Service, request.ReferenceType, request.ReferenceId);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var file = await repository.GetForUpdateAsync(fileId, cancellationToken) ?? throw NotFound();
        bool changed;
        try { changed = file.ReleaseAttachment(request.ReservationId, request.Service, request.ReferenceType, request.ReferenceId); }
        catch (InvalidOperationException exception) { throw Conflict(exception); }
        if (changed) await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<FileDownloadDto> DownloadAsync(Guid fileId, FileRecordDownloadRequest request, CancellationToken cancellationToken)
    {
        ValidateRecord(request.Service, request.ReferenceType, request.ReferenceId);
        var file = await repository.GetAsync(fileId, cancellationToken) ?? throw NotFound();
        // The calling service must authorize the caller against this exact business record first.
        if (file.Status != FileObjectStatus.Attached || file.AttachmentService != request.Service ||
            file.ReferenceType != request.ReferenceType || file.ReferenceId != request.ReferenceId) throw NotFound();
        try
        {
            var link = await storage.CreateDownloadRequestAsync(file.ObjectKey,
                TimeSpan.FromMinutes(Math.Clamp(options.Value.DownloadUrlMinutes, 1, 60)), cancellationToken);
            return new(link.Url, link.ExpiresAt);
        }
        catch (FileStorageUnavailableException)
        {
            throw new DomainException("FILE_STORAGE_UNAVAILABLE", "File storage is temporarily unavailable.", 503);
        }
    }

    private static void Validate(Guid reservationId, string service, string referenceType, Guid referenceId)
    {
        if (reservationId == Guid.Empty)
            throw new DomainException("VALIDATION_FAILED", "A reservation ID is required.", 422);
        ValidateRecord(service, referenceType, referenceId);
    }
    private static void ValidateRecord(string service, string referenceType, Guid referenceId)
    {
        if (referenceId == Guid.Empty ||
            service is not ("TaskExecution" or "Resource" or "Readiness" or "NotificationAudit") ||
            string.IsNullOrWhiteSpace(referenceType) || referenceType.Length > 50 || referenceType != referenceType.Trim())
            throw new DomainException("VALIDATION_FAILED", "Valid reservation, service and record identity are required.", 422);
    }
    private static DomainException NotFound() => new("FILE_NOT_FOUND", "The file does not exist for this record.", 404);
    private static DomainException Conflict(InvalidOperationException exception) => new("FILE_ATTACHMENT_CONFLICT", exception.Message, 409);
}
