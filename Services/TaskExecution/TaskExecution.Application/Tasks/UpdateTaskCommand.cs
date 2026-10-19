using TaskExecution.Application.Scopes;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Tasks;

public sealed record UpdateTaskCommand(
    Guid TaskId,
    string? Name,
    string? Description,
    string? CompletionCriteria,
    TaskPriority? Priority,
    int? EstimatedMinutes,
    int? RequiredVolunteers,
    DateTimeOffset? StartAt,
    DateTimeOffset? Deadline,
    int? ExpectedVersion);

public sealed class UpdateTaskCommandHandler(ITaskRepository tasks, TaskScopeAccess scopeAccess)
{
    public async Task<TaskDto> HandleAsync(
        UpdateTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdForUpdateAsync(command.TaskId, cancellationToken);
        if (task is null)
        {
            throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        }

        var (workArea, season) = await scopeAccess.GetWorkAreaForLeaderWriteAsync(
            task.WorkAreaId,
            cancellationToken);

        task.EnsurePlanningCanBeUpdated(command.ExpectedVersion);

        var nextStart = command.StartAt ?? task.StartAt;
        var nextDeadline = command.Deadline ?? task.Deadline;
        TaskScheduleRules.EnsureFitsScope(nextStart, nextDeadline, workArea, season);

        task.UpdatePlanning(
            command.Name,
            command.Description,
            command.CompletionCriteria,
            command.Priority,
            command.EstimatedMinutes,
            command.RequiredVolunteers,
            command.StartAt,
            command.Deadline,
            command.ExpectedVersion,
            DateTimeOffset.UtcNow);

        try
        {
            await tasks.SaveChangesAsync(cancellationToken);
        }
        catch (TaskConcurrencyException)
        {
            throw new DomainRuleException(
                "TASK_VERSION_MISMATCH",
                "The task was updated by another request.",
                DomainErrorKind.PreconditionFailed);
        }

        return TaskDto.From(task);
    }
}
