using BuildingBlocks.Security;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Communities.Commands.CreateCommunity;

public sealed class CreateCommunityCommandHandler(
    IParishRepository parishRepository,
    ICommunityRepository communityRepository,
    ICurrentUser currentUser)
{
    public async Task<CreateCommunityResult> HandleAsync(
        CreateCommunityCommand command,
        CancellationToken cancellationToken = default)
    {
        var parishExists = await parishRepository.ExistsAsync(
            command.ParishId,
            cancellationToken);

        if (!parishExists)
        {
            return CreateCommunityResult.ParishNotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, command.ParishId);

        var name = command.Name.Trim();
        if (await communityRepository.ExistsByNameAsync(
                command.ParishId,
                name,
                cancellationToken))
        {
            return CreateCommunityResult.NameConflict();
        }

        var community = Community.Create(
            command.ParishId,
            name,
            command.Description,
            DateTimeOffset.UtcNow);

        communityRepository.Add(community);

        try
        {
            await communityRepository.SaveChangesAsync(cancellationToken);
        }
        catch (CommunityNameConflictException)
        {
            return CreateCommunityResult.NameConflict();
        }

        return CreateCommunityResult.Success(CommunityDto.From(community));
    }
}
