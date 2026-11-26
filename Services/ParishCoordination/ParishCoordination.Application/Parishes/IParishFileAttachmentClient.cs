namespace ParishCoordination.Application.Parishes;

public sealed record ParishMapFileDto(Guid Id, Guid UploadedByUserId, string Purpose, string Status, string FileName,
    string? ReferenceType = null, Guid? ReferenceId = null);

public interface IParishFileAttachmentClient
{
    Task<ParishMapFileDto?> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default);

    Task ReserveAsync(Guid fileId, Guid parishId, Guid uploaderUserId, Guid reservationId, CancellationToken cancellationToken = default);
    Task FinalizeAsync(Guid fileId, Guid parishId, Guid reservationId, CancellationToken cancellationToken = default);

    Task ReleaseAsync(Guid fileId, Guid parishId, Guid reservationId, CancellationToken cancellationToken = default);
}
