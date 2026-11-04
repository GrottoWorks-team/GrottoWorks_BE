using System.Text.Json;
using Resource.Domain.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.Application.Scope;

public interface ITaskMaterialPlanStore
{
    Task<TaskMaterialPlan?> GetAsync(Guid taskId, CancellationToken cancellationToken);
    void Add(TaskMaterialPlan plan);
    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed class TaskMaterialPlanProjector(ITaskMaterialPlanStore store)
{
    public async Task ApplyAsync(Guid taskId, Guid workAreaId, Guid parishId, Guid seasonId, long version,
        IReadOnlyList<TaskMaterialSnapshot> materials, CancellationToken cancellationToken)
    {
        if (materials.Any(item => item is null)) throw new FormatException("Task material entries must not be null.");
        var inputs = materials.Select(item => new TaskMaterialPlanInput(item.MaterialId, item.RequiredQuantity, item.Note)).ToArray();
        var current = await store.GetAsync(taskId, cancellationToken);
        if (current is null) store.Add(TaskMaterialPlan.Create(taskId, workAreaId, parishId, seasonId, version, inputs, DateTimeOffset.UtcNow));
        else if (!current.Apply(workAreaId, parishId, seasonId, version, inputs, DateTimeOffset.UtcNow)) return;
        await store.SaveAsync(cancellationToken);
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
