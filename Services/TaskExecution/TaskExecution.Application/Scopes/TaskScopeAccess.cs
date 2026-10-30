using BuildingBlocks.Security;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;

namespace TaskExecution.Application.Scopes;

public sealed class TaskScopeAccess(IScopeProjectionStore scopes, ICurrentUser currentUser)
{
    public async Task<WorkAreaScope> GetWorkAreaForReadAsync(
        Guid workAreaId,
        CancellationToken cancellationToken = default)
    {
        var workArea = await scopes.GetWorkAreaAsync(workAreaId, cancellationToken);
        if (workArea is null)
        {
            throw DomainRuleException.NotFound("WORK_AREA_NOT_FOUND", "The work area does not exist.");
        }

        if (currentUser.Role != GrottoWorksRoles.Admin && currentUser.ParishId != workArea.ParishId)
        {
            throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED", "The work area belongs to another parish.");
        }

        return workArea;
    }

    public async Task<(WorkAreaScope WorkArea, SeasonScope Season)> GetWorkAreaForLeaderWriteAsync(
        Guid workAreaId,
        CancellationToken cancellationToken = default)
    {
        var workArea = await GetWorkAreaForReadAsync(workAreaId, cancellationToken);
        if (currentUser.Role != GrottoWorksRoles.Leader || workArea.LeaderUserId != currentUser.UserId)
        {
            throw DomainRuleException.Forbidden(
                "WORK_AREA_ACCESS_DENIED",
                "Only the leader assigned to this work area can manage its tasks.");
        }

        var season = await scopes.GetSeasonAsync(workArea.SeasonId, cancellationToken);
        if (season is null)
        {
            throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The owning season does not exist in TaskExecution.");
        }

        if (season.IsReadOnly)
        {
            throw new DomainRuleException("SEASON_READ_ONLY", "The archived season is read-only.");
        }

        return (workArea, season);
    }

    public async Task<WorkAreaScope> GetWorkAreaForLeaderReadAsync(Guid workAreaId, CancellationToken cancellationToken = default)
    {
        var area = await GetWorkAreaForReadAsync(workAreaId, cancellationToken);
        if (currentUser.Role != GrottoWorksRoles.Leader || area.LeaderUserId != currentUser.UserId)
            throw DomainRuleException.Forbidden("WORK_AREA_ACCESS_DENIED", "Only the assigned leader can read this information.");
        return area;
    }

    public async Task<(WorkAreaScope WorkArea, SeasonScope Season)> GetWorkAreaForVolunteerWriteAsync(
        Guid workAreaId, CancellationToken cancellationToken = default)
    {
        var area = await GetWorkAreaForReadAsync(workAreaId, cancellationToken);
        if (currentUser.Role != GrottoWorksRoles.Volunteer)
            throw DomainRuleException.Forbidden("WORK_AREA_ACCESS_DENIED", "Volunteer access is required.");
        var season = await scopes.GetSeasonAsync(area.SeasonId, cancellationToken)
            ?? throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The owning season is missing.");
        if (season.IsReadOnly) throw new DomainRuleException("SEASON_READ_ONLY", "The archived season is read-only.");
        return (area, season);
    }
}
