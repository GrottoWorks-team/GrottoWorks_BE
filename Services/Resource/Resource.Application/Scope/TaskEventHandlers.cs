using System.Text.Json;
using System.Text.Json.Serialization;
using Resource.Application.Abstractions;
using Resource.Application.Inventory;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.Application.Scope;

/// <summary>
/// Consumer side of TaskExecution's outbox events (<c>TaskIntegrationEvent</c>): keeps <c>task_projection</c> current between
/// full snapshots. Only tasks already projected are updated (the events carry no name or type); an older aggregate version
/// is ignored and re-delivery is harmless, so no inbox is needed. Other event types are ignored.
/// A task that becomes CANCELLED while it holds allocated materials raises <c>material.allocation.recall-required</c> once
/// (V-04): only the transition publishes, so a re-delivered or newer CANCELLED event does not repeat it.
/// </summary>
public sealed class ApplyTaskEventHandler(
    ITaskProjectionStore store,
    Purchases.PurchaseBuyerSync buyers,
    IAllocationStore allocations,
    IWorkAreaScopeStore scopes,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork)
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
        var previousStatus = task.Status;
        var changed = message.EventType == TaskEventTypes.StatusChanged
            ? task.ApplyStatus(Read<ProjectedTaskStatus>(message, "status"), message.AggregateVersion, now)
            : Read<string>(message, "decision") == "ACCEPTED" &&
              task.AddAssignee(Read<Guid>(message, "volunteerUserId"), message.AggregateVersion, now);
        if (!changed)
        {
            return false;
        }

        await buyers.ApplyAsync(task, cancellationToken);
        var cancelledNow = previousStatus != ProjectedTaskStatus.Cancelled && task.Status == ProjectedTaskStatus.Cancelled;
        var outstanding = cancelledNow ? await allocations.ListItemsOfTaskAsync(task.TaskId, cancellationToken) : [];
        var scope = outstanding.Count > 0 ? await scopes.GetAsync(task.WorkAreaId, cancellationToken) : null;
        await unitOfWork.ExecuteAsync(async () =>
        {
            await store.SaveChangesAsync(cancellationToken);
            if (scope is not null)
            {
                await publisher.PublishAsync(
                    MaterialAllocationRecallRequired.EventType,
                    MaterialAllocationRecallRequired.Version,
                    new MaterialAllocationRecallRequired(task.TaskId, scope.WorkAreaId, scope.SeasonId, scope.ParishId, scope.MaterialOfficerUserId,
                        outstanding.Select(item => new AllocationRecallLine(item.MaterialAllocationItemId, item.MaterialId, item.AllocatedQuantity)).ToList(),
                        now),
                    "task",
                    task.TaskId,
                    cancellationToken);
            }
        }, cancellationToken);
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
