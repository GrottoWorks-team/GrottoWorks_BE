using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using ParishCoordination.Application.Parishes;

namespace ParishCoordination.Application.Parishes;

public sealed record SaveParishMapCommand(Guid ParishId, Guid FileId, string FileName);

public sealed record ParishMapDto(Guid? ImageFileId, string? FileName, DateTimeOffset? UpdatedAt);

public sealed class SaveParishMapCommandHandler(
    IParishRepository parishRepository,
    IParishFileAttachmentClient fileClient,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public async Task<ParishMapDto> HandleAsync(SaveParishMapCommand command, CancellationToken cancellationToken)
    {
        ParishAccess.EnsureSameParish(currentUser, command.ParishId);

        var parish = await parishRepository.GetByIdForUpdateAsync(command.ParishId, cancellationToken)
            ?? throw new DomainException("PARISH_NOT_FOUND", "The parish does not exist.", 404);

        var file = await fileClient.GetFileAsync(command.FileId, cancellationToken)
            ?? throw new DomainException("FILE_NOT_FOUND", "The file does not exist.", 404);

        if (file.Status != "UPLOADED" || file.Purpose != "MAP_IMAGE")
            throw new DomainException("FILE_NOT_READY", "The map image is not uploaded yet.", 409);
        if (file.UploadedByUserId != currentUser.UserId)
            throw new DomainException("FILE_ACCESS_DENIED", "The file was uploaded by another user.", 403);

        var reservationId = Guid.NewGuid();
        try
        {
            await fileClient.AttachAsync(command.FileId, command.ParishId, currentUser.UserId, reservationId, cancellationToken);
        }
        catch
        {
            await fileClient.ReleaseAsync(command.FileId, command.ParishId, reservationId, cancellationToken);
            throw;
        }

        parish.SetMapImage(command.FileId, command.FileName, timeProvider.GetUtcNow());
        await parishRepository.SaveChangesAsync(cancellationToken);

        return new ParishMapDto(parish.MapImageFileId, parish.MapImageFileName, parish.UpdatedAt);
    }
}
