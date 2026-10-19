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
}
