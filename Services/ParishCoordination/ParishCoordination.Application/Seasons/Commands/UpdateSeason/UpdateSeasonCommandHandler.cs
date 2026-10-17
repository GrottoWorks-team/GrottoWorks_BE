using BuildingBlocks.Security;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Seasons.Commands.UpdateSeason;

public sealed class UpdateSeasonCommandHandler(
    ISeasonRepository seasonRepository,
    ICurrentUser currentUser)
{
    public async Task<UpdateSeasonResult> HandleAsync(
        UpdateSeasonCommand command,
        CancellationToken cancellationToken = default)
    {
        var season = await seasonRepository.GetByIdForUpdateAsync(
            command.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return UpdateSeasonResult.NotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);

        if (season.Status is not SeasonStatus.Planning)
        {
            return UpdateSeasonResult.InvalidState();
        }

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != season.Version)
        {
            return UpdateSeasonResult.VersionMismatch();
        }

        var name = command.Name?.Trim() ?? season.Name;
        var seasonYear = command.SeasonYear ?? season.SeasonYear;
        var startDate = command.StartDate ?? season.StartDate;
        var endDate = command.EndDate ?? season.EndDate;
        var description = command.ShouldUpdateDescription
            ? command.Description
            : season.Description;
        var estimatedBudget = command.EstimatedBudget ?? season.EstimatedBudget;

        if (endDate <= startDate)
        {
            return UpdateSeasonResult.InvalidDateRange();
        }

        if (!string.Equals(name, season.Name, StringComparison.Ordinal) &&
            await seasonRepository.ExistsByNameAsync(
                season.ParishId,
                name,
                cancellationToken,
                excludeSeasonId: season.Id))
        {
            return UpdateSeasonResult.NameConflict();
        }

        if (seasonYear != season.SeasonYear &&
            await seasonRepository.ExistsByYearAsync(
                season.ParishId,
                seasonYear,
                cancellationToken,
                excludeSeasonId: season.Id))
        {
            return UpdateSeasonResult.YearConflict();
        }

        season.Update(
            name,
            seasonYear,
            startDate,
            endDate,
            description,
            estimatedBudget,
            DateTimeOffset.UtcNow);

        try
        {
            await seasonRepository.SaveChangesAsync(cancellationToken);
        }
        catch (SeasonNameConflictException)
        {
            return UpdateSeasonResult.NameConflict();
        }
        catch (SeasonYearConflictException)
        {
            return UpdateSeasonResult.YearConflict();
        }
        catch (SeasonConcurrencyException)
        {
            return UpdateSeasonResult.VersionMismatch();
        }

        return UpdateSeasonResult.Success(SeasonDto.From(season));
    }
}
