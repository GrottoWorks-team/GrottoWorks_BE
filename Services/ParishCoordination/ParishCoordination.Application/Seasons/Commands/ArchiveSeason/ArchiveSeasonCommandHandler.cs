using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Seasons.Commands.ArchiveSeason;

public sealed class ArchiveSeasonCommandHandler(
    ISeasonRepository seasonRepository,
    ICurrentUser currentUser)
{
    public async Task<ArchiveSeasonResult> HandleAsync(
        ArchiveSeasonCommand command,
        CancellationToken cancellationToken = default)
    {
        var season = await seasonRepository.GetByIdForUpdateAsync(
            command.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return ArchiveSeasonResult.NotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);
        EnsureCanArchiveSeason();

        if (season.Status is not SeasonStatus.Completed)
        {
            return ArchiveSeasonResult.InvalidState();
        }

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != season.Version)
        {
            return ArchiveSeasonResult.VersionMismatch();
        }

        season.Archive(DateTimeOffset.UtcNow);

        try
        {
            await seasonRepository.SaveChangesAsync(cancellationToken);
        }
        catch (SeasonConcurrencyException)
        {
            return ArchiveSeasonResult.VersionMismatch();
        }

        return ArchiveSeasonResult.Success(SeasonDto.From(season));
    }

    private void EnsureCanArchiveSeason()
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
            "Only parish coordinators can archive a season.",
            StatusCodes.Status403Forbidden);
    }
}
