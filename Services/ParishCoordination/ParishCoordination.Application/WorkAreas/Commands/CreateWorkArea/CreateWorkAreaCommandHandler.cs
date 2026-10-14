using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.Communities;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreas.Commands.CreateWorkArea;

public sealed class CreateWorkAreaCommandHandler(
    ISeasonRepository seasonRepository,
    ICommunityRepository communityRepository,
    IWorkAreaRepository workAreaRepository,
    ICurrentUser currentUser)
{
    public async Task<CreateWorkAreaResult> HandleAsync(
        CreateWorkAreaCommand command,
        CancellationToken cancellationToken = default)
    {
        var season = await seasonRepository.GetByIdAsync(
            command.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return CreateWorkAreaResult.SeasonNotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);

        if (season.Status is not SeasonStatus.Planning)
        {
            return CreateWorkAreaResult.SeasonInvalidState();
        }

        if (command.StartDate.HasValue &&
            (command.StartDate.Value < season.StartDate || command.StartDate.Value > season.EndDate) ||
            command.Deadline.HasValue &&
            (command.Deadline.Value < season.StartDate || command.Deadline.Value > season.EndDate))
        {
            throw new DomainException(
                "WORK_AREA_DATE_OUT_OF_SEASON",
                "Work area dates must be within the season date range.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (command.StartDate.HasValue &&
            command.Deadline.HasValue &&
            command.Deadline.Value < command.StartDate.Value)
        {
            throw new DomainException(
                "WORK_AREA_INVALID_DATE_RANGE",
                "Deadline must not be before start date.",
                StatusCodes.Status422UnprocessableEntity);
        }

        Community? community = null;
        if (command.CommunityId.HasValue)
        {
            community = await communityRepository.GetByIdAsync(
                command.CommunityId.Value,
                cancellationToken);

            if (community is null)
            {
                return CreateWorkAreaResult.CommunityNotFound();
            }

            if (community.ParishId != season.ParishId)
            {
                throw new DomainException(
                    "PARISH_ACCESS_DENIED",
                    "The community does not belong to the season parish.",
                    StatusCodes.Status403Forbidden);
            }
        }

        var workArea = WorkArea.Create(
            command.SeasonId,
            command.CommunityId,
            command.CategoryId,
            null,
            null,
            command.Name.Trim(),
            CleanOptionalText(command.Description),
            command.StartDate,
            command.Deadline,
            WorkAreaStatus.Planned,
            DateTimeOffset.UtcNow);

        workAreaRepository.Add(workArea);
        await workAreaRepository.SaveChangesAsync(cancellationToken);

        return CreateWorkAreaResult.Success(WorkAreaDto.From(workArea));
    }

    private static string? CleanOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
