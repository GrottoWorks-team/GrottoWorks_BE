using BuildingBlocks.Security;

namespace ParishCoordination.Application.Communities.Commands.UpdateCommunity;

public sealed class UpdateCommunityCommandHandler(
    ICommunityRepository communityRepository,
    ICurrentUser currentUser)
{
    public async Task<UpdateCommunityResult> HandleAsync(
        UpdateCommunityCommand command,
        CancellationToken cancellationToken = default)
    {
        var community = await communityRepository.GetByIdForUpdateAsync(
            command.CommunityId,
            cancellationToken);

        if (community is null)
        {
            return UpdateCommunityResult.NotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, community.ParishId);

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != community.Version)
        {
            return UpdateCommunityResult.VersionMismatch();
        }

        var name = command.Name?.Trim() ?? community.Name;
        if (!string.Equals(name, community.Name, StringComparison.Ordinal) &&
            await communityRepository.ExistsByNameAsync(
                community.ParishId,
                name,
                cancellationToken))
        {
            return UpdateCommunityResult.NameConflict();
        }

        var description = command.ShouldUpdateDescription
            ? command.Description
            : community.Description;
        var status = command.Status ?? community.Status;

        community.Update(
            name,
            description,
            status,
            DateTimeOffset.UtcNow);

        try
        {
            await communityRepository.SaveChangesAsync(cancellationToken);
        }
        catch (CommunityNameConflictException)
        {
            return UpdateCommunityResult.NameConflict();
        }
        catch (CommunityConcurrencyException)
        {
            return UpdateCommunityResult.VersionMismatch();
        }

        return UpdateCommunityResult.Success(CommunityDto.From(community));
    }
}
