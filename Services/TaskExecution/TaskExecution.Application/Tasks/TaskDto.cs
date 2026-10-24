using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Tasks;

public sealed record TaskDto(
    Guid Id,
    Guid WorkAreaId,
    string Name,
    string? Description,
    string? CompletionCriteria,
    TaskType TaskType,
    Guid? PurchaseRequestId,
    TaskPriority Priority,
    int EstimatedMinutes,
    int RequiredVolunteers,
    DateTimeOffset? StartAt,
    DateTimeOffset Deadline,
    TaskState Status,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static TaskDto From(WorkTask task) =>
        new(
            task.Id,
            task.WorkAreaId,
            task.Name,
            task.Description,
            task.CompletionCriteria,
            task.Type,
            task.PurchaseRequestId,
            task.Priority,
            task.EstimatedMinutes,
            task.RequiredVolunteerCount,
            task.StartAt,
            task.Deadline,
            task.Status,
            task.Version,
            task.CreatedAt,
            task.UpdatedAt);
}
