using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Application.Seasons.Commands.CreateSeason;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Seasons.Commands.CloneSeason;

/// <summary>One work area of the source season the caller wants to copy (optionally renamed).</summary>
public sealed record CloneSeasonAreaInput(Guid SourceWorkAreaId, string? Name);

/// <summary><c>cloneSeason</c> (26/11/2026): new planning season + copies of the selected work areas.</summary>
public sealed record CloneSeasonCommand(
    Guid SourceSeasonId,
    string Name,
    int SeasonYear,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    decimal? EstimatedBudget,
    string DateShift,
    bool CopyAssignments,
    IReadOnlyList<CloneSeasonAreaInput>? Areas,
    Guid? RequestedParishId = null);

/// <summary>What the clone created — FE seeds its cache from this and copies map pins by id.</summary>
public sealed record CloneSeasonResult(SeasonDto Season, IReadOnlyList<WorkAreaDto> Areas, IReadOnlyList<string> Warnings)
{
    public static CloneSeasonResult Success(SeasonDto season, IReadOnlyList<WorkAreaDto> areas) => new(season, areas, []);
}

/// <summary>
/// Copies the source season into a new PLANNING season: every selected work area keeps its name, description,
/// category, dates (shifted to the new season window when asked), coordinates and — when
/// <see cref="CloneSeasonCommand.CopyAssignments"/> is set — its leader/material officer/community.
/// Work-area status always starts at PLANNING; readiness/checklist of the source season is not copied.
/// </summary>
public sealed class CloneSeasonCommandHandler(
    ISeasonRepository seasonRepository,
    IWorkAreaRepository workAreaRepository,
    ICurrentUser currentUser)
{
    public const string SameDayOfSeason = "SAME_DAY_OF_SEASON";

    public async Task<CloneSeasonResult> HandleAsync(CloneSeasonCommand command, CancellationToken cancellationToken = default)
    {
        if (currentUser.Role is not (GrottoWorksRoles.Admin or GrottoWorksRoles.Parish))
        {
            throw new DomainException("SEASON_CLONE_FORBIDDEN", "Only ADMIN or a parish board member can clone a season.",
                StatusCodes.Status403Forbidden);
        }

        var source = await seasonRepository.GetByIdAsync(command.SourceSeasonId, cancellationToken)
            ?? throw new DomainException("SOURCE_SEASON_NOT_FOUND", "The source season does not exist.",
                StatusCodes.Status404NotFound);
        ParishAccess.EnsureSameParish(currentUser, source.ParishId);

        if (command.StartDate >= command.EndDate)
        {
            throw new DomainException("SEASON_DATE_INVALID", "The new season must end after it starts.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var name = command.Name.Trim();
        if (name.Length == 0)
        {
            throw new DomainException("SEASON_NAME_REQUIRED", "A season name is required.", StatusCodes.Status422UnprocessableEntity);
        }

        if (await seasonRepository.ExistsByNameAsync(source.ParishId, name, cancellationToken))
        {
            throw new DomainException("SEASON_NAME_CONFLICT", "A season with this name already exists.", StatusCodes.Status409Conflict);
        }

        if (await seasonRepository.ExistsByYearAsync(source.ParishId, command.SeasonYear, cancellationToken))
        {
            throw new DomainException("SEASON_YEAR_CONFLICT", "A season of this year already exists.", StatusCodes.Status409Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        var season = Season.Create(source.ParishId, currentUser.UserId, name, command.SeasonYear,
            command.StartDate, command.EndDate, command.Description, command.EstimatedBudget, now);
        seasonRepository.Add(season);

        var (sourceAreas, _) = await workAreaRepository.GetPagedBySeasonIdAsync(source.Id, 1, 500, null, cancellationToken);
        var wanted = command.Areas is { Count: > 0 }
            ? command.Areas.ToDictionary(area => area.SourceWorkAreaId)
            : null;

        var created = new List<WorkArea>();
        foreach (var area in sourceAreas.Where(area => wanted is null || wanted.ContainsKey(area.Id)))
        {
            var overrideName = wanted is not null && wanted.TryGetValue(area.Id, out var input) ? input.Name : null;
            var (startDate, deadline) = ShiftDates(area, source, command);

            var copy = WorkArea.Create(
                season.Id,
                area.CommunityId,
                area.WorkAreaCategoryId,
                command.CopyAssignments ? area.LeaderUserId : null,
                command.CopyAssignments ? area.MaterialOfficerUserId : null,
                string.IsNullOrWhiteSpace(overrideName) ? area.Name : overrideName!.Trim(),
                area.Description,
                startDate,
                deadline,
                WorkAreaStatus.Planned,
                now,
                area.Latitude,
                area.Longitude,
                area.MapX,
                area.MapY);
            workAreaRepository.Add(copy);
            created.Add(copy);
        }

        await seasonRepository.SaveChangesAsync(cancellationToken);

        return CloneSeasonResult.Success(SeasonDto.From(season), created.Select(WorkAreaDto.From).ToList());
    }

    private static (DateOnly? Start, DateOnly? Deadline) ShiftDates(WorkArea area, Season source, CloneSeasonCommand command)
    {
        if (!string.Equals(command.DateShift, SameDayOfSeason, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        DateOnly? Shift(DateOnly? value) =>
            value is { } date ? command.StartDate.AddDays(date.DayNumber - source.StartDate.DayNumber) : null;

        return (Shift(area.StartDate), Shift(area.Deadline));
    }
}
