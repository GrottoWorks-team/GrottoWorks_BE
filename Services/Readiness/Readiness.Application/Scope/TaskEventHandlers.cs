using System.Text.Json;
using System.Text.Json.Serialization;
using Readiness.Domain;
using Readiness.Domain.Scope;
using TaskExecution.Contracts.Events;

namespace Readiness.Application.Scope;

/// <summary>
/// Consumer side of TaskExecution's outbox events (<c>TaskIntegrationEvent</c>): keeps <c>task_projection</c> current between
/// full snapshots. Only tasks already projected are updated (the events carry no name or type); an older aggregate version
/// is ignored and re-delivery is harmless, so no inbox is needed. Other event types are ignored.
/// </summary>
public sealed class ApplyTaskEventHandler(ITaskProjectionStore store, Checklists.CorrectiveTaskSync corrective)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) }
    };

    /// <returns>true when the projection changed.</returns>
    public async Task<bool> HandleAsync(TaskIntegrationEvent message, CancellationToken cancellationToken = default)
    {
        if (message.EventType is not (TaskEventTypes.StatusChanged or TaskEventTypes.AssignmentResponded))
        {
            return false;
        }

        var task = await store.GetAsync(message.AggregateId, cancellationToken);
        if (task is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var changed = message.EventType == TaskEventTypes.StatusChanged
            ? task.ApplyStatus(Read<ProjectedTaskStatus>(message, "status"), message.AggregateVersion, now)
            : Read<string>(message, "decision") == "ACCEPTED" &&
              task.AddAssignee(Read<Guid>(message, "volunteerUserId"), message.AggregateVersion, now);
        if (!changed)
        {
            return false;
        }

        await corrective.ApplyAsync(task, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static T Read<T>(TaskIntegrationEvent message, string property)
    {
        try
        {
            if (message.Payload.ValueKind == JsonValueKind.Object && message.Payload.TryGetProperty(property, out var value))
            {
                return value.Deserialize<T>(JsonOptions)!;
            }
        }
        catch (JsonException)
        {
        }

        throw DomainRuleException.Validation("EVENT_PAYLOAD_INVALID", $"{message.EventType} has no valid '{property}'.");
    }
}
