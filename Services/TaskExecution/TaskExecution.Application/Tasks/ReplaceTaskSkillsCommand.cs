using TaskExecution.Application.Assignments;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Tasks;

public sealed record ReplaceTaskSkillsCommand(
    Guid TaskId,
    IReadOnlyList<TaskSkillRequirement> Requirements,
    int? ExpectedVersion);

public sealed class ReplaceTaskSkillsCommandHandler(
    ITaskRepository tasks,
    TaskScopeAccess scopeAccess,
    IAssignmentStore assignments,
    IIdentityUserLookup identity)
{
    public Task<TaskDto> HandleAsync(
        ReplaceTaskSkillsCommand command,
        CancellationToken cancellationToken = default) =>
        assignments.InTaskTransactionAsync(command.TaskId,
            () => ReplaceUnderLockAsync(command, cancellationToken), cancellationToken);

    private async Task<TaskDto> ReplaceUnderLockAsync(
        ReplaceTaskSkillsCommand command, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdForUpdateAsync(command.TaskId, cancellationToken);
        if (task is null)
        {
            throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        }

        var (area, _) = await scopeAccess.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId, cancellationToken);

        var changed = task.ReplaceSkills(
            command.Requirements,
            command.ExpectedVersion,
            DateTimeOffset.UtcNow);

        if (!changed)
        {
            return TaskDto.From(task);
        }

        if (task.TeamLeaderAssignmentId is { } assignmentId)
        {
            var assignment = await assignments.GetForUpdateAsync(assignmentId, cancellationToken);
            var volunteer = assignment is null ? null :
                await identity.FindAsync(assignment.VolunteerId, cancellationToken);
            if (assignment is null || !TaskTeamLeaderEligibility.IsEligible(task, assignment, volunteer, area.ParishId))
                task.ClearInvalidTeamLeaderForCurrentChange(assignmentId);
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
