using BuildingBlocks.Security;

namespace ParishCoordination.Application.Parishes.Commands.UpdateParish;

public sealed class UpdateParishCommandHandler(
    IParishRepository parishRepository,
    ICurrentUser currentUser)
{
    public async Task<UpdateParishResult> HandleAsync(
        UpdateParishCommand command,
        CancellationToken cancellationToken = default)
    {
        var parish = await parishRepository.GetByIdForUpdateAsync(
            command.ParishId,
            cancellationToken);

        if (parish is null)
        {
            return new UpdateParishResult(UpdateParishOutcome.NotFound, null);
        }

        ParishAccess.EnsureSameParish(currentUser, parish.Id);

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != parish.Version)
        {
            return new UpdateParishResult(UpdateParishOutcome.VersionMismatch, null);
        }

        var name = command.Name ?? parish.Name;
        var address = command.ShouldUpdateAddress ? command.Address : parish.Address;
        var description = command.ShouldUpdateDescription
            ? command.Description
            : parish.Description;
        var status = command.Status ?? parish.Status;

        parish.Update(
            name,
            address,
            description,
            status,
            DateTimeOffset.UtcNow);

        try
        {
            await parishRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ParishConcurrencyException)
        {
            return new UpdateParishResult(UpdateParishOutcome.VersionMismatch, null);
        }

        return new UpdateParishResult(
            UpdateParishOutcome.Success,
            ParishDto.From(parish));
    }
}
