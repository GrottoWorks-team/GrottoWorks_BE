using System.ComponentModel.DataAnnotations;

namespace NotificationAudit.Contracts.Files;

public sealed record ReserveFileAttachmentRequest(Guid ReservationId, Guid UploadedByUserId,
    [property: Required, StringLength(50)] string Service,
    [property: Required, StringLength(50)] string ReferenceType, Guid ReferenceId, FilePurposeDto Purpose);

public sealed record FileAttachmentCommand(Guid ReservationId,
    [property: Required, StringLength(50)] string Service,
    [property: Required, StringLength(50)] string ReferenceType, Guid ReferenceId);

public sealed record FileRecordDownloadRequest(
    [property: Required, StringLength(50)] string Service,
    [property: Required, StringLength(50)] string ReferenceType, Guid ReferenceId);

public sealed record FileAttachmentDto(Guid FileId, Guid ReservationId, string Service,
    string ReferenceType, Guid ReferenceId, bool Finalized);
