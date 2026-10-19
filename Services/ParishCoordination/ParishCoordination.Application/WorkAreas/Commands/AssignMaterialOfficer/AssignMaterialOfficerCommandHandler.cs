using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Application.Users;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreas.Commands.AssignMaterialOfficer;

public sealed class AssignMaterialOfficerCommandHandler(
    IWorkAreaRepository workAreaRepository,
    ISeasonRepository seasonRepository,
    IIdentityUserLookup identityUserLookup,
    ICurrentUser currentUser)
{
    public async Task<AssignMaterialOfficerResult> HandleAsync(
        AssignMaterialOfficerCommand command,
        CancellationToken cancellationToken = default)
    {
        var workArea = await workAreaRepository.GetByIdForUpdateAsync(
            command.WorkAreaId,
            cancellationToken);

        if (workArea is null)
        {
            return AssignMaterialOfficerResult.WorkAreaNotFound();
        }

        var season = await seasonRepository.GetByIdAsync(
            workArea.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return AssignMaterialOfficerResult.SeasonNotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);
        EnsureCanAssignMaterialOfficer();

        if (season.Status is SeasonStatus.Archived)
        {
            return AssignMaterialOfficerResult.SeasonInvalidState();
        }

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != workArea.Version)
        {
            return AssignMaterialOfficerResult.VersionMismatch();
        }

        IdentityUser? materialOfficer;

        try
        {
            materialOfficer = await identityUserLookup.GetByIdAsync(
                command.MaterialOfficerUserId,
                cancellationToken);
        }
        catch (IdentityUserLookupUnavailableException)
        {
            return AssignMaterialOfficerResult.IdentityServiceUnavailable();
        }

        if (materialOfficer is null)
        {
            return AssignMaterialOfficerResult.UserNotFound();
        }

        if (materialOfficer.Status is not IdentityUserStatus.Active)
        {
            return AssignMaterialOfficerResult.UserNotActive();
        }

        if (!string.Equals(
                materialOfficer.Role,
                GrottoWorksRoles.MaterialOfficer,
                StringComparison.Ordinal))
        {
            return AssignMaterialOfficerResult.UserRoleMismatch();
        }

        if (materialOfficer.ParishId != season.ParishId)
        {
            throw new DomainException(
                "PARISH_ACCESS_DENIED",
                "The material officer and work area must belong to the same parish.",
                StatusCodes.Status403Forbidden);
        }

        var assignmentChanged = workArea.AssignMaterialOfficer(
            materialOfficer.UserId,
            DateTimeOffset.UtcNow);

        if (!assignmentChanged)
        {
            return AssignMaterialOfficerResult.Success(WorkAreaDto.From(workArea));
        }

        try
        {
            await workAreaRepository.SaveChangesAsync(cancellationToken);
        }
        catch (WorkAreaConcurrencyException)
        {
            return AssignMaterialOfficerResult.VersionMismatch();
        }

        return AssignMaterialOfficerResult.Success(WorkAreaDto.From(workArea));
    }

    private void EnsureCanAssignMaterialOfficer()
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
            "Only parish coordinators can assign a work area material officer.",
            StatusCodes.Status403Forbidden);
    }
}
