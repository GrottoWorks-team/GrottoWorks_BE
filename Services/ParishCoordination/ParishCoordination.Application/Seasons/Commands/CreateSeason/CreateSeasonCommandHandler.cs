using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Seasons.Commands.CreateSeason;

public sealed class CreateSeasonCommandHandler(
    IParishRepository parishRepository,
    ISeasonRepository seasonRepository,
    ICurrentUser currentUser)
{
    public async Task<CreateSeasonResult> HandleAsync(
        CreateSeasonCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
        {
            ParishAccess.EnsureSameParish(currentUser, null);
        }

        var isAdmin = string.Equals(
            currentUser.Role,
            GrottoWorksRoles.Admin,
            StringComparison.Ordinal);

        if (isAdmin && (!command.RequestedParishId.HasValue || command.RequestedParishId == Guid.Empty))
        {
            throw new DomainException(
                "PARISH_ID_REQUIRED",
                "parishId is required for ADMIN.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (!isAdmin &&
            command.RequestedParishId.HasValue &&
            command.RequestedParishId != currentUser.ParishId)
        {
            throw new DomainException(
                "PARISH_ACCESS_DENIED",
                "A PARISH user cannot create a season outside the parish in the JWT.",
                StatusCodes.Status403Forbidden);
        }

        var targetParishId = isAdmin
            ? command.RequestedParishId
            : currentUser.ParishId;

        ParishAccess.EnsureSameParish(currentUser, targetParishId);

        var parishId = targetParishId!.Value;
        if (!await parishRepository.ExistsAsync(parishId, cancellationToken))
        {
            return CreateSeasonResult.ParishNotFound();
        }

        var name = command.Name.Trim();
        if (await seasonRepository.ExistsByNameAsync(parishId, name, cancellationToken))
        {
            return CreateSeasonResult.NameConflict();
        }

        if (await seasonRepository.ExistsByYearAsync(parishId, command.SeasonYear, cancellationToken))
        {
            return CreateSeasonResult.YearConflict();
        }

        var season = Season.Create(
            parishId,
            currentUser.UserId,
            name,
            command.SeasonYear,
            command.StartDate,
            command.EndDate,
            command.Description,
            command.EstimatedBudget,
            DateTimeOffset.UtcNow);

        seasonRepository.Add(season);

        try
        {
            await seasonRepository.SaveChangesAsync(cancellationToken);
        }
        catch (SeasonNameConflictException)
        {
            return CreateSeasonResult.NameConflict();
        }
        catch (SeasonYearConflictException)
        {
            return CreateSeasonResult.YearConflict();
        }

        return CreateSeasonResult.Success(SeasonDto.From(season));
    }
}