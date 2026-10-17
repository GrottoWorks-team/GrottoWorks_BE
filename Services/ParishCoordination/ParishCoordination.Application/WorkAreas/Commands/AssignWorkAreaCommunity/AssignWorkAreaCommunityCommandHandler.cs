using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.Communities;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaCommunity;

public sealed class AssignWorkAreaCommunityCommandHandler(
    IWorkAreaRepository workAreaRepository,
    ISeasonRepository seasonRepository,
    ICommunityRepository communityRepository,
    ICurrentUser currentUser)
{
    public async Task<AssignWorkAreaCommunityResult> HandleAsync(
        AssignWorkAreaCommunityCommand command,
        CancellationToken cancellationToken = default)
    {
        var workArea = await workAreaRepository.GetByIdForUpdateAsync(
            command.WorkAreaId,
            cancellationToken);

        if (workArea is null)
        {
            return AssignWorkAreaCommunityResult.WorkAreaNotFound();
        }

        var season = await seasonRepository.GetByIdAsync(
            workArea.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return AssignWorkAreaCommunityResult.SeasonNotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);
        EnsureCanAssignCommunity();

        if (season.Status is SeasonStatus.Archived)
        {
            return AssignWorkAreaCommunityResult.SeasonInvalidState();
        }

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != workArea.Version)
        {
            return AssignWorkAreaCommunityResult.VersionMismatch();
        }

        var community = await communityRepository.GetByIdAsync(
            command.CommunityId,
            cancellationToken);

        if (community is null)
        {
            return AssignWorkAreaCommunityResult.CommunityNotFound();
        }

        if (community.ParishId != season.ParishId)
        {
            throw new DomainException(
                "PARISH_ACCESS_DENIED",
                "The community and work area must belong to the same parish.",
                StatusCodes.Status403Forbidden);
        }

        if (community.Status is not CommunityStatus.Active)
        {
            return AssignWorkAreaCommunityResult.CommunityInactive();
        }

        var assignmentChanged = workArea.AssignCommunity(
            community.Id,
            DateTimeOffset.UtcNow);

        if (!assignmentChanged)
        {
            return AssignWorkAreaCommunityResult.Success(WorkAreaDto.From(workArea));
        }

        try
        {
            await workAreaRepository.SaveChangesAsync(cancellationToken);
        }
        catch (WorkAreaConcurrencyException)
        {
            return AssignWorkAreaCommunityResult.VersionMismatch();
        }

        return AssignWorkAreaCommunityResult.Success(WorkAreaDto.From(workArea));
    }

    private void EnsureCanAssignCommunity()
    {
        var isAdmin = string.Equals(
            currentUser.Role,
            GrottoWorksRoles.Admin,
            StringComparison.Ordinal);
        var isParishUser = string.Equals(
            currentUser.Role,
            GrottoWorksRoles.Parish,
            StringComparison.Ordinal);

        if (isAdmin || isParishUser)
        {
            return;
        }

        throw new DomainException(
            "WORK_AREA_ACCESS_DENIED",
            "Only parish coordinators can assign a work area community.",
            StatusCodes.Status403Forbidden);
    }
}
