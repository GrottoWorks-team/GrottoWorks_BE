using Resource.Contracts.Catalog;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Tasks;

public interface IMaterialCatalogLookup
{
    Task<IReadOnlyList<InternalMaterialDto>> FindAsync(IReadOnlyCollection<Guid> materialIds, CancellationToken cancellationToken);
}

public sealed record ReplaceTaskMaterialsCommand(Guid TaskId, IReadOnlyList<TaskMaterialRequirement> Materials, int? ExpectedVersion);

public sealed class ReplaceTaskMaterialsHandler(ITaskRepository tasks, TaskScopeAccess access,
    IMaterialCatalogLookup catalog, TaskEventRecorder events)
{
    public async Task<TaskDto> HandleAsync(ReplaceTaskMaterialsCommand command, CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdForUpdateAsync(command.TaskId, cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        var (area, _) = await access.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId, cancellationToken);
        task.EnsurePlanningCanBeUpdated(command.ExpectedVersion);
        var requirements = WorkTask.NormalizeMaterials(command.Materials);
        if (requirements.Count > 0)
        {
            var materials = (await catalog.FindAsync(requirements.Select(row => row.MaterialId).ToArray(), cancellationToken))
                .ToDictionary(row => row.MaterialId);
            foreach (var requirement in requirements)
            {
                if (!materials.TryGetValue(requirement.MaterialId, out var material))
                    throw DomainRuleException.NotFound("MATERIAL_NOT_FOUND", $"Material {requirement.MaterialId} does not exist.");
                if (material.Status != "ACTIVE")
                    throw new DomainRuleException("MATERIAL_INACTIVE", $"Material {requirement.MaterialId} is inactive.");
            }
        }
        if (!task.ReplaceMaterials(requirements, command.ExpectedVersion, DateTimeOffset.UtcNow)) return TaskDto.From(task);
        events.Record(TaskEventTypes.MaterialsChanged, task, area, new
        {
            taskId = task.Id, workAreaId = area.WorkAreaId,
            materials = task.Materials.OrderBy(row => row.MaterialId).Select(row => new
            { materialId = row.MaterialId, requiredQuantity = row.RequiredQuantity, note = row.Note }).ToArray()
        });
        try { await tasks.SaveChangesAsync(cancellationToken); }
        catch (TaskConcurrencyException)
        {
            throw new DomainRuleException("TASK_VERSION_MISMATCH", "The task changed concurrently.", DomainErrorKind.PreconditionFailed);
        }
        return TaskDto.From(task);
    }
}
