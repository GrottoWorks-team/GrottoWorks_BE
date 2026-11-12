using BuildingBlocks.Security;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Assignments;

public interface ITaskTeamLeaderQuery
{
    Task<IReadOnlyList<Guid>> FindActiveTaskIdsAsync(Guid volunteerId, CancellationToken cancellationToken);
}

internal static class TaskTeamLeaderEligibility
{
    public static bool IsEligible(WorkTask task, TaskAssignment assignment, IdentityUser? volunteer, Guid parishId) =>
        assignment.TaskId == task.Id &&
        assignment.Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress &&
        volunteer is { Status: "ACTIVE", Role: GrottoWorksRoles.Volunteer } &&
        volunteer.ParishId == parishId &&
        (task.Skills.Count == 0 || task.Skills.Any(skill =>
            VolunteerSkills.Meets(volunteer, skill.SkillId, skill.RequiredSkillLevel)));
}

public sealed class TaskTeamLeaderService(ITaskRepository tasks, IAssignmentStore assignments,
    ITaskTeamLeaderQuery teamLeaderQuery, IScopeSnapshotTransaction scopeTransaction, IScopeProjectionStore scopes,
    TaskScopeAccess access, ActiveUserAccess activeUsers, IIdentityUserLookup identity)
{
    public async Task<TaskDto> SetAsync(Guid taskId, Guid assignmentId, int version, CancellationToken cancellationToken)
    {
        // Only the immutable volunteer ID is used before taking the locks. Check scope and
        // reload the assignment under the task lock before using any eligibility information.
        var candidate = await assignments.GetAsync(assignmentId, cancellationToken);
        if (candidate is null)
        {
            return await assignments.InTaskTransactionAsync<TaskDto>(taskId, async () =>
            {
                await LoadEditableTaskAsync(taskId, version, cancellationToken);
                throw MissingAssignment();
            }, cancellationToken);
        }

        return await scopeTransaction.ExecuteAsync("user", candidate.VolunteerId, () =>
            assignments.InTaskTransactionAsync(taskId, async () =>
            {
                var (task, parishId) = await LoadEditableTaskAsync(taskId, version, cancellationToken);
                var assignment = await assignments.GetForUpdateAsync(assignmentId, cancellationToken)
                    ?? throw MissingAssignment();
                if (assignment.TaskId != task.Id)
                    throw new DomainRuleException("TASK_TEAM_LEADER_ASSIGNMENT_MISMATCH",
                        "The assignment belongs to another task.");

                if (assignment.Status is not (AssignmentStatus.Accepted or AssignmentStatus.InProgress))
                    throw NotEligible();

                var volunteer = await identity.FindAsync(assignment.VolunteerId, cancellationToken);
                if (!TaskTeamLeaderEligibility.IsEligible(task, assignment, volunteer, parishId))
                    throw NotEligible();

                if (task.SetTeamLeader(assignment.Id, version, DateTimeOffset.UtcNow))
                    await assignments.SaveAsync(cancellationToken);

                return TaskDto.From(task);
            }, cancellationToken), cancellationToken);
    }

    public Task<TaskDto> ClearAsync(Guid taskId, int version, CancellationToken cancellationToken) =>
        assignments.InTaskTransactionAsync(taskId, async () =>
        {
            var (task, _) = await LoadEditableTaskAsync(taskId, version, cancellationToken);
            if (task.SetTeamLeader(null, version, DateTimeOffset.UtcNow))
                await assignments.SaveAsync(cancellationToken);

            return TaskDto.From(task);
        }, cancellationToken);

    // The Identity inbox owns the transaction and holds the same volunteer lock used by PUT.
    internal async Task ReconcileVolunteerAsync(Guid volunteerId, CancellationToken cancellationToken)
    {
        var taskIds = await teamLeaderQuery.FindActiveTaskIdsAsync(volunteerId, cancellationToken);
        if (taskIds.Count == 0)
            return;

        var volunteer = await identity.FindAsync(volunteerId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var taskId in taskIds.Order())
        {
            await assignments.InTaskTransactionAsync(taskId, async () =>
            {
                var task = await tasks.GetByIdForUpdateAsync(taskId, cancellationToken);
                if (task is null || task.Status is TaskState.Completed or TaskState.Cancelled ||
                    task.TeamLeaderAssignmentId is not { } assignmentId)
                    return false;

                var assignment = await assignments.GetForUpdateAsync(assignmentId, cancellationToken);
                if (assignment is null || assignment.VolunteerId != volunteerId)
                    return false;

                var area = await scopes.GetWorkAreaAsync(task.WorkAreaId, cancellationToken)
                    ?? throw new InvalidOperationException("The designated task is missing its work area scope.");
                if (TaskTeamLeaderEligibility.IsEligible(task, assignment, volunteer, area.ParishId))
                    return false;

                task.SetTeamLeader(null, task.Version, now);
                await assignments.SaveAsync(cancellationToken);
                return true;
            }, cancellationToken);
        }
    }

    private async Task<(WorkTask Task, Guid ParishId)> LoadEditableTaskAsync(
        Guid taskId, int version, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdForUpdateAsync(taskId, cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        var (area, _) = await access.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId, cancellationToken);
        await activeUsers.EnsureCallerAsync(area.ParishId, cancellationToken);
        task.EnsureTeamLeaderCanBeEdited(version);
        return (task, area.ParishId);
    }

    private static DomainRuleException MissingAssignment() =>
        DomainRuleException.NotFound("TASK_ASSIGNMENT_NOT_FOUND", "The assignment does not exist.");

    private static DomainRuleException NotEligible() =>
        new("TASK_TEAM_LEADER_NOT_ELIGIBLE", "The assignment is not eligible to lead this task.");
}
