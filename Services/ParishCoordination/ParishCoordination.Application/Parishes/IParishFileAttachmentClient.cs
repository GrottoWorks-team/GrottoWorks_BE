namespace ParishCoordination.Application.Parishes;

public sealed record ParishMapFileDto(Guid Id, Guid UploadedByUserId, string Purpose, string Status, string FileName);

public interface IParishFileAttachmentClient
{
    Task<ParishMapFileDto?> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default);

    Task AttachAsync(Guid fileId, Guid parishId, Guid uploaderUserId, Guid reservationId, CancellationToken cancellationToken = default);

    Task ReleaseAsync(Guid fileId, Guid parishId, Guid reservationId, CancellationToken cancellationToken = default);
}
