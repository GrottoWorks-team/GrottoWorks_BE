using TaskExecution.Application.Scopes;
using TaskExecution.Domain;

namespace TaskExecution.Application.Tasks;

public sealed record ReplaceCompletionCriteriaCommand(
    Guid TaskId,
    string? CompletionCriteria,
    int? ExpectedVersion);

public sealed class ReplaceCompletionCriteriaCommandHandler(
    ITaskRepository tasks,
    TaskScopeAccess scopeAccess)
{
    public async Task<TaskDto> HandleAsync(
        ReplaceCompletionCriteriaCommand command,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdForUpdateAsync(command.TaskId, cancellationToken);
        if (task is null)
        {
            throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        }

        await scopeAccess.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId, cancellationToken);

        var changed = task.ReplaceCompletionCriteria(
            command.CompletionCriteria,
            command.ExpectedVersion,
            DateTimeOffset.UtcNow);

        if (!changed)
        {
            return TaskDto.From(task);
        }

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
