using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Seasons.Commands.ActivateSeason;

public sealed class ActivateSeasonCommandHandler(
    ISeasonRepository seasonRepository,
    IWorkAreaRepository workAreaRepository,
    ICurrentUser currentUser)
{
    public async Task<ActivateSeasonResult> HandleAsync(
        ActivateSeasonCommand command,
        CancellationToken cancellationToken = default)
    {
        var season = await seasonRepository.GetByIdForUpdateAsync(
            command.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return ActivateSeasonResult.NotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);
        EnsureCanActivateSeason();

        if (season.Status is not SeasonStatus.Planning)
        {
            return ActivateSeasonResult.InvalidState();
        }

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != season.Version)
        {
            return ActivateSeasonResult.VersionMismatch();
        }

        var readiness = await workAreaRepository.GetActivationReadinessAsync(
            season.Id,
            cancellationToken);

        if (readiness.TotalWorkAreas == 0)
        {
            return ActivateSeasonResult.WorkAreaRequired();
        }

        if (readiness.IncompleteAssignments > 0)
        {
            return ActivateSeasonResult.WorkAreaAssignmentIncomplete();
        }

        var hasActivePeriodConflict = await seasonRepository.HasOverlappingActiveSeasonAsync(
            season.ParishId,
            season.StartDate,
            season.EndDate,
            season.Id,
            cancellationToken);

        if (hasActivePeriodConflict)
        {
            return ActivateSeasonResult.ActivePeriodConflict();
        }

        season.Activate(DateTimeOffset.UtcNow);

        try
        {
            await seasonRepository.SaveChangesAsync(cancellationToken);
        }
        catch (SeasonConcurrencyException)
        {
            return ActivateSeasonResult.VersionMismatch();
        }

        return ActivateSeasonResult.Success(SeasonDto.From(season));
    }

    private void EnsureCanActivateSeason()
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
            "SEASON_ACCESS_DENIED",
            "Only parish coordinators can activate a season.",
            StatusCodes.Status403Forbidden);
    }
}
