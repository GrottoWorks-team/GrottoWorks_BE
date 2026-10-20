using TaskExecution.Application.Scopes;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Tasks;

public sealed record ReplaceTaskSkillsCommand(
    Guid TaskId,
    IReadOnlyList<TaskSkillRequirement> Requirements,
    int? ExpectedVersion);

public sealed class ReplaceTaskSkillsCommandHandler(
    ITaskRepository tasks,
    TaskScopeAccess scopeAccess)
{
    public async Task<TaskDto> HandleAsync(
        ReplaceTaskSkillsCommand command,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdForUpdateAsync(command.TaskId, cancellationToken);
        if (task is null)
        {
            throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        }

        await scopeAccess.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId, cancellationToken);

        var changed = task.ReplaceSkills(
            command.Requirements,
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
