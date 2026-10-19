using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Seasons.Commands.CompleteSeason;

public sealed class CompleteSeasonCommandHandler(
    ISeasonRepository seasonRepository,
    ICurrentUser currentUser)
{
    public async Task<CompleteSeasonResult> HandleAsync(
        CompleteSeasonCommand command,
        CancellationToken cancellationToken = default)
    {
        var season = await seasonRepository.GetByIdForUpdateAsync(
            command.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return CompleteSeasonResult.NotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);
        EnsureCanCompleteSeason();

        if (season.Status is not SeasonStatus.Ready)
        {
            return CompleteSeasonResult.InvalidState();
        }

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != season.Version)
        {
            return CompleteSeasonResult.VersionMismatch();
        }

        season.Complete(DateTimeOffset.UtcNow);

        try
        {
            await seasonRepository.SaveChangesAsync(cancellationToken);
        }
        catch (SeasonConcurrencyException)
        {
            return CompleteSeasonResult.VersionMismatch();
        }

        return CompleteSeasonResult.Success(SeasonDto.From(season));
    }

    private void EnsureCanCompleteSeason()
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
            "Only parish coordinators can complete a season.",
            StatusCodes.Status403Forbidden);
    }
}
