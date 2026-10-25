using TaskExecution.Application.Scopes;
using TaskExecution.Domain;

namespace TaskExecution.Application.Tasks;

public sealed record ReadyTaskCommand(Guid TaskId, int? ExpectedVersion);

public sealed class ReadyTaskCommandHandler(
    ITaskRepository tasks,
    TaskScopeAccess scopeAccess)
{
    public async Task<TaskDto> HandleAsync(
        ReadyTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdForUpdateAsync(command.TaskId, cancellationToken);
        if (task is null)
        {
            throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        }

        await scopeAccess.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId, cancellationToken);

        task.MarkReady(command.ExpectedVersion, DateTimeOffset.UtcNow);

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
