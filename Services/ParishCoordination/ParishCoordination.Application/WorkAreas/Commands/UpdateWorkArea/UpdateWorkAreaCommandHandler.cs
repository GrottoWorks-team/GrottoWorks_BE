using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Application.WorkAreaCategories;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreas.Commands.UpdateWorkArea;

public sealed class UpdateWorkAreaCommandHandler(
    IWorkAreaRepository workAreaRepository,
    ISeasonRepository seasonRepository,
    IWorkAreaCategoryRepository categoryRepository,
    ICurrentUser currentUser)
{
    public async Task<UpdateWorkAreaResult> HandleAsync(
        UpdateWorkAreaCommand command,
        CancellationToken cancellationToken = default)
    {
        var workArea = await workAreaRepository.GetByIdForUpdateAsync(
            command.WorkAreaId,
            cancellationToken);

        if (workArea is null)
        {
            return UpdateWorkAreaResult.NotFound();
        }

        var season = await seasonRepository.GetByIdAsync(
            workArea.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return UpdateWorkAreaResult.SeasonNotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, season.ParishId);
        EnsureCanUpdateWorkArea(workArea.LeaderUserId);

        if (season.Status is SeasonStatus.Archived)
        {
            return UpdateWorkAreaResult.SeasonInvalidState();
        }

        if (command.ExpectedVersion.HasValue &&
            command.ExpectedVersion.Value != workArea.Version)
        {
            return UpdateWorkAreaResult.VersionMismatch();
        }

        var name = command.Name?.Trim() ?? workArea.Name;
        var description = command.ShouldUpdateDescription
            ? command.Description
            : workArea.Description;
        var categoryId = workArea.WorkAreaCategoryId;
        var startDate = command.StartDate ?? workArea.StartDate;
        var deadline = command.Deadline ?? workArea.Deadline;

        if (command.CategoryId.HasValue &&
            command.CategoryId != workArea.WorkAreaCategoryId)
        {
            var category = await categoryRepository.GetByIdAsync(
                command.CategoryId.Value,
                cancellationToken);

            if (category is null)
            {
                return UpdateWorkAreaResult.CategoryNotFound();
            }

            if (category.Status is not WorkAreaCategoryStatus.Active)
            {
                return UpdateWorkAreaResult.CategoryInactive();
            }

            categoryId = category.Id;
        }

        if (command.StartDate.HasValue || command.Deadline.HasValue)
        {
            EnsureValidDates(
                startDate,
                deadline,
                season.StartDate,
                season.EndDate);
        }

        workArea.Update(
            name,
            description,
            categoryId,
            startDate,
            deadline,
            DateTimeOffset.UtcNow);

        try
        {
            await workAreaRepository.SaveChangesAsync(cancellationToken);
        }
        catch (WorkAreaConcurrencyException)
        {
            return UpdateWorkAreaResult.VersionMismatch();
        }

        return UpdateWorkAreaResult.Success(WorkAreaDto.From(workArea));
    }

    private void EnsureCanUpdateWorkArea(Guid? leaderUserId)
    {
        if (string.Equals(currentUser.Role, GrottoWorksRoles.Admin, StringComparison.Ordinal) ||
            string.Equals(currentUser.Role, GrottoWorksRoles.Parish, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(currentUser.Role, GrottoWorksRoles.Leader, StringComparison.Ordinal) &&
            leaderUserId == currentUser.UserId)
        {
            return;
        }

        throw new DomainException(
            "WORK_AREA_ACCESS_DENIED",
            "Only the assigned leader can update this work area.",
            StatusCodes.Status403Forbidden);
    }

    private static void EnsureValidDates(
        DateOnly? startDate,
        DateOnly? deadline,
        DateOnly seasonStartDate,
        DateOnly seasonEndDate)
    {
        var startsOutsideSeason = startDate.HasValue &&
            (startDate.Value < seasonStartDate || startDate.Value > seasonEndDate);
        var endsOutsideSeason = deadline.HasValue &&
            (deadline.Value < seasonStartDate || deadline.Value > seasonEndDate);

        if (startsOutsideSeason || endsOutsideSeason)
        {
            throw new DomainException(
                "WORK_AREA_DATE_OUT_OF_SEASON",
                "Work area dates must be within the season date range.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (startDate.HasValue &&
            deadline.HasValue &&
            deadline.Value < startDate.Value)
        {
            throw new DomainException(
                "WORK_AREA_INVALID_DATE_RANGE",
                "Deadline must not be before start date.",
                StatusCodes.Status422UnprocessableEntity);
        }
    }
}
