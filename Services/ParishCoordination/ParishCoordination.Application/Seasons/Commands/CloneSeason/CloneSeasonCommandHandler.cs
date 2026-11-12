using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.Users;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Seasons.Commands.CloneSeason;

/// <summary>
/// F-SEA-xx "Tạo mùa mới từ mùa cũ".
/// One HTTP write = one transaction: the season, every copied work area and the outbox
/// rows are persisted by a single <c>SaveChangesAsync</c> (N+1 inserts, no N+1 requests).
/// </summary>
public sealed class CloneSeasonCommandHandler(
    ISeasonRepository seasonRepository,
    IWorkAreaRepository workAreaRepository,
    IIdentityUserBatchLookup identityUserBatchLookup,
    ICurrentUser currentUser)
{
    /// <summary>Safety net: a season never holds more areas than this in practice.</summary>
    public const int MaxSourceAreas = 500;

    public async Task<CloneSeasonResult> HandleAsync(
        CloneSeasonCommand command,
        CancellationToken cancellationToken = default)
    {
        var source = await seasonRepository.GetByIdAsync(
            command.SourceSeasonId,
            cancellationToken);

        if (source is null)
        {
            return CloneSeasonResult.SourceSeasonNotFound();
        }

        ParishAccess.EnsureSameParish(currentUser, source.ParishId);

        EnsureCanClone();

        if (command.EndDate <= command.StartDate)
        {
            throw new DomainException(
                "SEASON_DATE_INVALID",
                "End date must be after start date.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var name = command.Name.Trim();
        if (name.Length == 0)
        {
            throw new DomainException(
                "SEASON_NAME_REQUIRED",
                "Season name is required.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (await seasonRepository.ExistsByNameAsync(source.ParishId, name, cancellationToken))
        {
            return CloneSeasonResult.NameConflict();
        }

        if (await seasonRepository.ExistsByYearAsync(
                source.ParishId,
                command.SeasonYear,
                cancellationToken))
        {
            return CloneSeasonResult.YearConflict();
        }

        var sourceAreas = await workAreaRepository.ListBySeasonIdAsync(
            source.Id,
            cancellationToken);

        if (sourceAreas.Count > MaxSourceAreas)
        {
            throw new DomainException(
                "CLONE_SOURCE_TOO_LARGE",
                $"The source season has more than {MaxSourceAreas} work areas.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var selected = SelectAreas(command.Areas, sourceAreas);
        var warnings = new List<string>();
        var assignments = await ResolveAssignmentsAsync(
            selected,
            source.ParishId,
            command.CopyAssignments,
            warnings,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var season = Season.Create(
            source.ParishId,
            currentUser.UserId,
            name,
            command.SeasonYear,
            command.StartDate,
            command.EndDate,
            string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            command.EstimatedBudget,
            now);

        seasonRepository.Add(season);

        var created = new List<WorkAreaDto>(selected.Count);
        foreach (var (sourceArea, selection) in selected)
        {
            var assignment = assignments.GetValueOrDefault(sourceArea.Id);
            var (startDate, deadline) = ShiftDates(
                sourceArea.StartDate,
                sourceArea.Deadline,
                source,
                command);

            var area = WorkArea.Create(
                season.Id,
                selection?.CommunityId ?? sourceArea.CommunityId,
                selection?.CategoryId ?? sourceArea.WorkAreaCategoryId,
                assignment?.LeaderId,
                assignment?.MaterialOfficerId,
                string.IsNullOrWhiteSpace(selection?.Name)
                    ? sourceArea.Name
                    : selection!.Name!.Trim(),
                sourceArea.Description,
                startDate,
                deadline,
                WorkAreaStatus.Planned,
                now);

            workAreaRepository.Add(area);
            created.Add(WorkAreaDto.From(area));
        }

        try
        {
            // Single transaction for the season, all areas and the outbox rows.
            await seasonRepository.SaveChangesAsync(cancellationToken);
        }
        catch (SeasonNameConflictException)
        {
            return CloneSeasonResult.NameConflict();
        }
        catch (SeasonYearConflictException)
        {
            return CloneSeasonResult.YearConflict();
        }

        return CloneSeasonResult.Success(SeasonDto.From(season), created, warnings);
    }

    private void EnsureCanClone()
    {
        if (!currentUser.IsAuthenticated)
        {
            ParishAccess.EnsureSameParish(currentUser, null);
        }

        if (!string.Equals(currentUser.Role, GrottoWorksRoles.Admin, StringComparison.Ordinal) &&
            !string.Equals(currentUser.Role, GrottoWorksRoles.Parish, StringComparison.Ordinal))
        {
            throw new DomainException(
                "SEASON_CLONE_FORBIDDEN",
                "Only ADMIN or a parish board member can clone a season.",
                StatusCodes.Status403Forbidden);
        }
    }

    private static List<(WorkArea Source, CloneAreaSelection? Selection)> SelectAreas(
        IReadOnlyList<CloneAreaSelection>? requested,
        IReadOnlyList<WorkArea> sourceAreas)
    {
        if (requested is null || requested.Count == 0)
        {
            return sourceAreas.Select(area => (area, (CloneAreaSelection?)null)).ToList();
        }

        if (requested.Select(item => item.SourceWorkAreaId).Distinct().Count() != requested.Count)
        {
            throw new DomainException(
                "CLONE_AREA_DUPLICATE",
                "A work area was selected more than once.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var byId = sourceAreas.ToDictionary(area => area.Id);
        var selected = new List<(WorkArea, CloneAreaSelection?)>(requested.Count);

        foreach (var item in requested)
        {
            if (!byId.TryGetValue(item.SourceWorkAreaId, out var sourceArea))
            {
                throw new DomainException(
                    "CLONE_AREA_NOT_IN_SOURCE",
                    $"Work area '{item.SourceWorkAreaId}' does not belong to the source season.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            selected.Add((sourceArea, item));
        }

        return selected;
    }

    private async Task<Dictionary<Guid, CloneAssignment>> ResolveAssignmentsAsync(
        IReadOnlyList<(WorkArea Source, CloneAreaSelection? Selection)> selected,
        Guid parishId,
        bool copyAssignments,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, CloneAssignment>(selected.Count);

        if (!copyAssignments)
        {
            foreach (var (sourceArea, _) in selected)
            {
                result[sourceArea.Id] = new CloneAssignment(null, null);
            }

            return result;
        }

        var userIds = selected
            .SelectMany(item => new[] { item.Source.LeaderUserId, item.Source.MaterialOfficerUserId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        // One internal call for the whole set (chunked by the client), never N calls.
        var users = userIds.Count == 0
            ? new Dictionary<Guid, IdentityUser>()
            : new Dictionary<Guid, IdentityUser>(
                await identityUserBatchLookup.GetByIdsAsync(userIds, cancellationToken));

        foreach (var (sourceArea, _) in selected)
        {
            var leaderId = Verify(
                sourceArea.LeaderUserId,
                users,
                parishId,
                GrottoWorksRoles.Leader,
                sourceArea.Name,
                "WORK_AREA_LEADER_NOT_VERIFIED",
                warnings);

            var officerId = Verify(
                sourceArea.MaterialOfficerUserId,
                users,
                parishId,
                GrottoWorksRoles.MaterialOfficer,
                sourceArea.Name,
                "WORK_AREA_MATERIAL_OFFICER_NOT_VERIFIED",
                warnings);

            result[sourceArea.Id] = new CloneAssignment(leaderId, officerId);
        }

        return result;
    }

    /// <summary>
    /// Mirrors <c>PUT /work-areas/{id}/leader</c> validation: the user must exist, be ACTIVE,
    /// hold the expected role and belong to the same parish. Otherwise the copy is dropped
    /// and the caller receives a warning instead of a silently broken assignment.
    /// </summary>
    private static Guid? Verify(
        Guid? userId,
        IReadOnlyDictionary<Guid, IdentityUser> users,
        Guid parishId,
        string requiredRole,
        string areaName,
        string warningCode,
        List<string> warnings)
    {
        if (!userId.HasValue)
        {
            return null;
        }

        if (users.TryGetValue(userId.Value, out var user) &&
            user.Status == IdentityUserStatus.Active &&
            string.Equals(user.Role, requiredRole, StringComparison.OrdinalIgnoreCase) &&
            user.ParishId == parishId)
        {
            return user.UserId;
        }

        warnings.Add($"{warningCode}:{areaName}");
        return null;
    }

    private static (DateOnly? StartDate, DateOnly? Deadline) ShiftDates(
        DateOnly? sourceStartDate,
        DateOnly? sourceDeadline,
        Season sourceSeason,
        CloneSeasonCommand command)
    {
        if (command.DateShift == CloneDateShift.None)
        {
            return (null, null);
        }

        return (
            Shift(sourceStartDate),
            Shift(sourceDeadline));

        DateOnly? Shift(DateOnly? sourceDate)
        {
            if (!sourceDate.HasValue)
            {
                return null;
            }

            var offset = sourceDate.Value.DayNumber - sourceSeason.StartDate.DayNumber;
            var shifted = command.StartDate.AddDays(offset);

            // Never let an area fall outside the new season window (avoids
            // WORK_AREA_DATE_OUT_OF_SEASON when the leader edits the area later).
            if (shifted < command.StartDate)
            {
                shifted = command.StartDate;
            }
            else if (shifted > command.EndDate)
            {
                shifted = command.EndDate;
            }

            return shifted;
        }
    }

    private sealed record CloneAssignment(Guid? LeaderId, Guid? MaterialOfficerId);
}
