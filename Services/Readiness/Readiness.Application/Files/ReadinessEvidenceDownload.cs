using BuildingBlocks.Security;
using Readiness.Application.Checklists;
using Readiness.Application.Scope;
using Readiness.Domain;

namespace Readiness.Application.Files;

public sealed class ReadinessEvidenceDownload(ChecklistLoader loader, ITaskProjectionStore tasks, ICurrentUser user, IRecordFileDownload files)
{
    public async Task<RecordFileDownload> GetAsync(Guid checkId, Guid itemId, Guid fileId, CancellationToken ct)
    {
        var (checklist, scope) = await loader.ForReadAsync(checkId, ct);
        var item = checklist.Item(itemId);
        var allowed = user.Role == GrottoWorksRoles.Leader && scope.LeaderUserId == user.UserId ||
            user.Role == GrottoWorksRoles.MaterialOfficer && scope.MaterialOfficerUserId == user.UserId ||
            user.Role == GrottoWorksRoles.Parish && user.ParishId == scope.ParishId ||
            user.Role == GrottoWorksRoles.Volunteer && item.CorrectiveTaskId is { } taskId &&
                (await tasks.GetAsync(taskId, ct))?.AssigneeUserIds.Contains(user.UserId) == true;
        if (!allowed || !item.EvidenceFileIds.Contains(fileId)) throw ChecklistLoader.NotFound();
        return await files.GetAsync(fileId, itemId, ct);
    }
}
