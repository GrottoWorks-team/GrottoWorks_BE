using System.Text.Json;
using BuildingBlocks.Security;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Events;

public interface ITaskEventOutbox
{
    void Add(TaskIntegrationEvent message);
}

public interface ITaskCorrelationContext
{
    string CorrelationId { get; }
}

public sealed class TaskEventRecorder(ITaskEventOutbox outbox, ICurrentUser user, ITaskCorrelationContext context)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(string eventType, WorkTask task, WorkAreaScope area, object payload)
    {
        outbox.Add(new TaskIntegrationEvent(Guid.NewGuid(), eventType, 1, DateTimeOffset.UtcNow,
            context.CorrelationId, user.UserId, area.ParishId, area.SeasonId,
            "task", task.Id, task.Version, JsonSerializer.SerializeToElement(payload, JsonOptions)));
    }
}
