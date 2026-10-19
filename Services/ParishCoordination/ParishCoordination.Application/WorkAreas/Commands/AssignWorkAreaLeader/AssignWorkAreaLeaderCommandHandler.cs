using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Application.Users;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaLeader;

public sealed class AssignWorkAreaLeaderCommandHandler(
    IWorkAreaRepository workAreaRepository,
    ISeasonRepository seasonRepository,
    IIdentityUserLookup identityUserLookup,
    ICurrentUser currentUser)
{
    public async Task<AssignWorkAreaLeaderResult> HandleAsync(
        AssignWorkAreaLeaderCommand command,
        CancellationToken cancellationToken = default)
    {
        var workArea = await workAreaRepository.GetByIdForUpdateAsync(
            command.WorkAreaId,
            cancellationToken);

        if (workArea is null)
        {
            return AssignWorkAreaLeaderResult.WorkAreaNotFound();
        }

        var season = await seasonRepository.GetByIdAsync(
            workArea.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return AssignWorkAreaLeaderResult.SeasonNotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);
        EnsureCanAssignLeader();

        if (season.Status is SeasonStatus.Archived)
        {
            return AssignWorkAreaLeaderResult.SeasonInvalidState();
        }

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != workArea.Version)
        {
            return AssignWorkAreaLeaderResult.VersionMismatch();
        }

        IdentityUser? leader;

        try
        {
            leader = await identityUserLookup.GetByIdAsync(
                command.LeaderUserId,
                cancellationToken);
        }
        catch (IdentityUserLookupUnavailableException)
        {
            return AssignWorkAreaLeaderResult.IdentityServiceUnavailable();
        }

        if (leader is null)
        {
            return AssignWorkAreaLeaderResult.UserNotFound();
        }

        if (leader.Status is not IdentityUserStatus.Active)
        {
            return AssignWorkAreaLeaderResult.UserNotActive();
        }

        if (!string.Equals(
                leader.Role,
                GrottoWorksRoles.Leader,
                StringComparison.Ordinal))
        {
            return AssignWorkAreaLeaderResult.UserRoleMismatch();
        }

        if (leader.ParishId != season.ParishId)
        {
            throw new DomainException(
                "PARISH_ACCESS_DENIED",
                "The leader and work area must belong to the same parish.",
                StatusCodes.Status403Forbidden);
        }

        var assignmentChanged = workArea.AssignLeader(
            leader.UserId,
            DateTimeOffset.UtcNow);

        if (!assignmentChanged)
        {
            return AssignWorkAreaLeaderResult.Success(WorkAreaDto.From(workArea));
        }

        try
        {
            await workAreaRepository.SaveChangesAsync(cancellationToken);
        }
        catch (WorkAreaConcurrencyException)
        {
            return AssignWorkAreaLeaderResult.VersionMismatch();
        }

        return AssignWorkAreaLeaderResult.Success(WorkAreaDto.From(workArea));
    }

    private void EnsureCanAssignLeader()
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
            "Only parish coordinators can assign a work area leader.",
            StatusCodes.Status403Forbidden);
    }
}
