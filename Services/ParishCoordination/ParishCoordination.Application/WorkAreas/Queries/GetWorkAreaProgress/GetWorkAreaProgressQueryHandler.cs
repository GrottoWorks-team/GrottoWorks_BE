using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using ParishCoordination.Application.Seasons;

namespace ParishCoordination.Application.WorkAreas.Queries.GetWorkAreaProgress;

public sealed class GetWorkAreaProgressQueryHandler(
    IWorkAreaRepository workAreaRepository,
    ISeasonRepository seasonRepository,
    IWorkAreaTaskCountClient taskCountClient,
    ICurrentUser currentUser)
{
    public async Task<GetWorkAreaProgressResult> HandleAsync(
        GetWorkAreaProgressQuery query,
        CancellationToken cancellationToken = default)
    {
        var workArea = await workAreaRepository.GetByIdAsync(
            query.WorkAreaId,
            cancellationToken);

        if (workArea is null)
        {
            return GetWorkAreaProgressResult.WorkAreaNotFound();
        }

        var season = await seasonRepository.GetByIdAsync(
            workArea.SeasonId,
            cancellationToken);

        if (season is null)
        {
            return GetWorkAreaProgressResult.SeasonNotFound();
        }

        EnsureCanReadProgress(season.ParishId, workArea.LeaderUserId);

        WorkAreaTaskCounts taskCounts;
        try
        {
            taskCounts = await taskCountClient.GetAsync(
                workArea.Id,
                cancellationToken);
        }
        catch (WorkAreaTaskCountUnavailableException)
        {
            return GetWorkAreaProgressResult.TaskExecutionUnavailable();
        }

        return GetWorkAreaProgressResult.Success(new WorkAreaProgressDto(
            workArea.Id,
            workArea.SeasonId,
            workArea.Status,
            taskCounts.TotalTasks,
            taskCounts.CancelledTasks,
            taskCounts.EligibleTasks,
            taskCounts.CompletedTasks,
            taskCounts.OverdueTasks,
            taskCounts.CalculatedAt));
    }

    private void EnsureCanReadProgress(Guid parishId, Guid? leaderUserId)
    {
        if (string.Equals(currentUser.Role, GrottoWorksRoles.Parish, StringComparison.Ordinal))
        {
            ParishAccess.EnsureSameParish(currentUser, parishId);
            return;
        }

        if (string.Equals(currentUser.Role, GrottoWorksRoles.Leader, StringComparison.Ordinal))
        {
            ParishAccess.EnsureSameParish(currentUser, parishId);
            if (leaderUserId == currentUser.UserId)
            {
                return;
            }

            throw new DomainException(
                "WORK_AREA_ACCESS_DENIED",
                "Only the assigned leader can view progress for this work area.",
                StatusCodes.Status403Forbidden);
        }

        throw new DomainException(
            "WORK_AREA_ACCESS_DENIED",
            "Only parish coordinators and assigned leaders can view work area progress.",
            StatusCodes.Status403Forbidden);
    }
}
