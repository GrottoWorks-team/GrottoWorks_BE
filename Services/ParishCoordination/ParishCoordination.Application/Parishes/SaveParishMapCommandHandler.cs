using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;

namespace ParishCoordination.Application.Parishes;

public sealed record SaveParishMapCommand(Guid ParishId, Guid FileId, string FileName);

public sealed record ParishMapDto(Guid? ImageFileId, string? FileName, DateTimeOffset? UpdatedAt);

public sealed class SaveParishMapCommandHandler(
    IParishRepository parishRepository,
    IParishFileAttachmentClient fileClient,
    IParishMapAttachmentStore intents,
    ParishMapAttachmentRecovery recovery,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public async Task<ParishMapDto> HandleAsync(SaveParishMapCommand command, CancellationToken cancellationToken)
    {
        ParishAccess.EnsureSameParish(currentUser, command.ParishId);

        await using var lease = await intents.LockAsync(command.ParishId, cancellationToken);
        var parish = await parishRepository.GetByIdForUpdateAsync(command.ParishId, cancellationToken)
            ?? throw new DomainException("PARISH_NOT_FOUND", "The parish does not exist.", 404);

        var file = await fileClient.GetFileAsync(command.FileId, cancellationToken)
            ?? throw new DomainException("FILE_NOT_FOUND", "The file does not exist.", 404);

        if (file.UploadedByUserId != currentUser.UserId)
            throw new DomainException("FILE_ACCESS_DENIED", "The file was uploaded by another user.", 403);
        var alreadyAttached = file.Status == "ATTACHED" && file.ReferenceType == "PARISH" &&
            file.ReferenceId == command.ParishId;
        if (file.Purpose != "MAP_IMAGE" || (file.Status != "UPLOADED" && !alreadyAttached))
            throw new DomainException("FILE_NOT_READY", "The map image is not ready for this parish.", 409);

        var intent = await intents.FindAsync(command.ParishId, command.FileId, cancellationToken);
        if (intent?.MapSaved == true)
        {
            await TryFinalizeAsync(intent, cancellationToken);
            return new ParishMapDto(parish.MapImageFileId, parish.MapImageFileName, parish.UpdatedAt);
        }

        if (!alreadyAttached)
        {
            if (intent is null)
            {
                intent = new ParishMapAttachmentIntent
                {
                    Id = Guid.NewGuid(), ParishId = command.ParishId, FileId = command.FileId,
                    NextAttemptAt = timeProvider.GetUtcNow().AddSeconds(30)
                };
                intents.Add(intent);
                await parishRepository.SaveChangesAsync(cancellationToken);
            }

            try
            {
                await fileClient.ReserveAsync(command.FileId, command.ParishId, currentUser.UserId, intent.Id, cancellationToken);
            }
            catch (DomainException error) when (error.StatusCode is 403 or 404 or 409 or 422)
            {
                // A definite refusal did not reserve the file for this operation.
                await intents.RemoveAsync(intent, cancellationToken);
                throw;
            }

            intent.MapSaved = true;
        }

        var changed = parish.MapImageFileId != command.FileId;
        if (changed)
            parish.SetMapImage(command.FileId, command.FileName, timeProvider.GetUtcNow());
        if (!alreadyAttached || changed)
            await parishRepository.SaveChangesAsync(cancellationToken);

        if (!alreadyAttached && intent is not null)
            await TryFinalizeAsync(intent, cancellationToken);

        return new ParishMapDto(parish.MapImageFileId, parish.MapImageFileName, parish.UpdatedAt);
    }

    private async Task TryFinalizeAsync(ParishMapAttachmentIntent intent, CancellationToken ct)
    {
        try
        {
            await recovery.FinalizeAsync(intent, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            throw new DomainException("FILE_ATTACHMENT_PENDING", "The map was saved and its attachment is being finalized.", 503);
        }
    }
}
