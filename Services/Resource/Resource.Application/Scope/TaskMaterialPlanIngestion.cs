using System.Text.Json;
using Resource.Domain.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.Application.Scope;

public interface ITaskMaterialPlanStore
{
    Task<TaskMaterialPlan?> GetAsync(Guid taskId, CancellationToken cancellationToken);
    /// <summary>
    /// Tổng nhu cầu vật tư của các nhiệm vụ **chưa huỷ** trong khu — nguồn để đồng bộ `material_requirement`
    /// (13/11/2026: trước đây nhu cầu khu không tính nhu cầu của nhiệm vụ nên báo \"đủ vật tư\" sai).
    /// </summary>
    Task<IReadOnlyList<TaskMaterialPlanTotal>> GetAreaTotalsAsync(Guid workAreaId, CancellationToken cancellationToken);
    void Add(TaskMaterialPlan plan);
    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed record TaskMaterialPlanTotal(Guid MaterialId, decimal RequiredQuantity);

public sealed class TaskMaterialPlanProjector(ITaskMaterialPlanStore store, TaskMaterialAreaRequirementSync areaRequirements)
{
    public async Task ApplyAsync(Guid taskId, Guid workAreaId, Guid parishId, Guid seasonId, long version,
        IReadOnlyList<TaskMaterialSnapshot> materials, CancellationToken cancellationToken)
    {
        if (materials.Any(item => item is null)) throw new FormatException("Task material entries must not be null.");
        var inputs = materials.Select(item => new TaskMaterialPlanInput(item.MaterialId, item.RequiredQuantity, item.Note)).ToArray();
        var current = await store.GetAsync(taskId, cancellationToken);
        var changed = false;
        if (current is null)
        {
            store.Add(TaskMaterialPlan.Create(taskId, workAreaId, parishId, seasonId, version, inputs, DateTimeOffset.UtcNow));
            changed = true;
        }
        else
        {
            changed = current.Apply(workAreaId, parishId, seasonId, version, inputs, DateTimeOffset.UtcNow);
        }

        if (!changed) return;
        await store.SaveAsync(cancellationToken);
        // 13/11/2026: nhu cầu của khu phải bao gồm nhu cầu của nhiệm vụ (nếu không sẽ báo “đủ vật tư” sai).
        await areaRequirements.SyncAsync(workAreaId, inputs.Select(item => item.MaterialId).ToArray(),
            DateTimeOffset.UtcNow, cancellationToken);
    }
}

public sealed class TaskMaterialEventIngestion(IScopeReplicationInbox inbox, TaskMaterialPlanProjector projector)
{
    public Task HandleAsync(TaskIntegrationEvent message, CancellationToken cancellationToken)
    {
        if (message.EventType != TaskEventTypes.MaterialsChanged) return Task.CompletedTask;
        if (message.Version != 1 || message.EventId == Guid.Empty || message.AggregateId == Guid.Empty ||
            message.AggregateType != "task" || message.AggregateVersion < 1 || message.ParishId == Guid.Empty || message.SeasonId == Guid.Empty)
            throw new FormatException("Invalid task material event envelope.");
        MaterialPayload? payload;
        try { payload = message.Payload.Deserialize<MaterialPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException exception) { throw new FormatException("Invalid task material event payload.", exception); }
        if (payload is null || payload.TaskId != message.AggregateId || payload.WorkAreaId == Guid.Empty || payload.Materials is null)
            throw new FormatException("Task material event identity is inconsistent.");
        return inbox.ExecuteAsync("resource-task-materials", message.EventId, message.SeasonId,
            () => projector.ApplyAsync(payload.TaskId, payload.WorkAreaId, message.ParishId, message.SeasonId,
                message.AggregateVersion, payload.Materials, cancellationToken), cancellationToken);
    }
    private sealed record MaterialPayload(Guid TaskId, Guid WorkAreaId, IReadOnlyList<TaskMaterialSnapshot>? Materials);
}
