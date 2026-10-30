using BuildingBlocks.Security;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Assignments;

public sealed record AssignVolunteerCommand(Guid TaskId, Guid VolunteerId, string? Note,
    bool OverrideCapacity, bool OverrideSkillMismatch, string? OverrideReason);

public sealed record AssignmentDto(Guid Id, Guid TaskId, Guid VolunteerId, AssignmentType AssignmentType,
    AssignmentStatus Status, string? Note, int Version, DateTimeOffset? RequestedAt,
    DateTimeOffset? AssignedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? RejectedAt,
    IReadOnlyList<string> Warnings, string? ResponseReason)
{
    public static AssignmentDto From(TaskAssignment row, IReadOnlyList<string>? warnings = null) => new(
        row.Id, row.TaskId, row.VolunteerId, row.Type, row.Status, row.Note, row.Version,
        row.RequestedAt, row.AssignedAt, row.ApprovedAt, row.RejectedAt, warnings ?? [], row.ResponseReason);
}

public sealed class AssignmentService(IAssignmentStore assignments, ITaskRepository tasks,
    TaskScopeAccess scopeAccess, ActiveUserAccess users, ICurrentUser user, TaskEventRecorder events)
{
    public async Task<AssignmentDto> RespondAsync(Guid assignmentId, bool accept, string? reason, int? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var assignment = await assignments.GetAsync(assignmentId, cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_ASSIGNMENT_NOT_FOUND", "The assignment does not exist.");
        if (user.Role != GrottoWorksRoles.Volunteer || assignment.VolunteerId != user.UserId)
            throw DomainRuleException.Forbidden("TASK_ASSIGNMENT_ACCESS_DENIED", "Only the assigned volunteer can respond.");
        return await assignments.InTaskTransactionAsync(assignment.TaskId, async () =>
        {
            var task = await tasks.GetByIdForUpdateAsync(assignment.TaskId, cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
            var (area, _) = await scopeAccess.GetWorkAreaForVolunteerWriteAsync(task.WorkAreaId, cancellationToken);
            await users.EnsureCallerAsync(area.ParishId, cancellationToken);
            if (task.Status is TaskExecution.Domain.Tasks.TaskStatus.Cancelled or TaskExecution.Domain.Tasks.TaskStatus.Completed)
                throw new DomainRuleException("TASK_INVALID_STATE", "The task no longer accepts assignment responses.");
            if (accept) assignment.Accept(expectedVersion, DateTimeOffset.UtcNow);
            else assignment.Decline(reason!, expectedVersion, DateTimeOffset.UtcNow);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            events.Record(TaskEventTypes.AssignmentResponded, task, area, new
            {
                assignmentId = assignment.Id, taskId = task.Id, workAreaId = area.WorkAreaId,
                volunteerUserId = user.UserId, decision = accept ? "ACCEPTED" : "DECLINED", reason = assignment.ResponseReason
            });
            await assignments.SaveAsync(cancellationToken);
            return AssignmentDto.From(assignment);
        }, cancellationToken);
    }

    public async Task<AssignmentDto> AssignAsync(AssignVolunteerCommand command, CancellationToken cancellationToken = default)
    {
        return await assignments.InTaskTransactionAsync(command.TaskId, async () =>
        {
            var task = await tasks.GetByIdForUpdateAsync(command.TaskId, cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
            var (area, _) = await scopeAccess.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId, cancellationToken);
            await users.EnsureCallerAsync(area.ParishId, cancellationToken);
            var volunteer = await users.EnsureActiveAsync(command.VolunteerId, area.ParishId,
                GrottoWorksRoles.Volunteer, cancellationToken);
            task.EnsurePlanningCanBeUpdated(null);

            var existing = await assignments.ListAsync(task.Id, cancellationToken);
            EnsureNoDuplicate(existing, command.VolunteerId);
            EnsureCapacity(existing, task.RequiredVolunteerCount, command.OverrideCapacity);
            EnsureSkills(task, volunteer, command.OverrideSkillMismatch);

            var assignment = TaskAssignment.Assign(task.Id, volunteer.UserId, user.UserId, command.Note,
                command.OverrideCapacity, command.OverrideSkillMismatch, command.OverrideReason, DateTimeOffset.UtcNow);
            var warnings = new List<string>();
            if (await assignments.HasScheduleConflictAsync(volunteer.UserId, task.Id, task.StartAt, task.Deadline, cancellationToken))
                warnings.Add("SCHEDULE_CONFLICT");
            assignments.Add(assignment);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            events.Record(TaskEventTypes.AssignmentCreated, task, area, new
            {
                assignmentId = assignment.Id, taskId = task.Id, workAreaId = area.WorkAreaId,
                volunteerUserId = volunteer.UserId, status = "PENDING", assignmentType = "DIRECT"
            });
            await assignments.SaveAsync(cancellationToken);
            return AssignmentDto.From(assignment, warnings);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<AssignmentDto>> ListAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdAsync(taskId, cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        await scopeAccess.GetWorkAreaForLeaderReadAsync(task.WorkAreaId, cancellationToken);
        return (await assignments.ListAsync(taskId, cancellationToken))
            .Where(row => row.Type == AssignmentType.Direct || row.Status != AssignmentStatus.Pending)
            .Select(row => AssignmentDto.From(row)).ToList();
    }

    public static void EnsureNoDuplicate(IReadOnlyList<TaskAssignment> existing, Guid volunteerId)
    {
        if (existing.Any(row => row.VolunteerId == volunteerId &&
            row.Status is AssignmentStatus.Pending or AssignmentStatus.Accepted or AssignmentStatus.InProgress or AssignmentStatus.Completed))
            throw new DomainRuleException("TASK_ASSIGNMENT_DUPLICATE", "The volunteer already has an active assignment or application.");
    }

    public static void EnsureCapacity(IReadOnlyList<TaskAssignment> existing, int capacity, bool allowOverride)
    {
        if (!allowOverride && existing.Count(row => row.ReservesCapacity) >= capacity)
            throw new DomainRuleException("TASK_CAPACITY_EXCEEDED", "The task has no remaining volunteer slots.");
    }

    public static void EnsureSkills(WorkTask task, IdentityUser volunteer, bool allowOverride)
    {
        if (task.Skills.Any(skill => skill.RequiredSkillLevel != null))
            throw new DomainRuleException("SKILL_LEVEL_VALIDATION_UNAVAILABLE", "Identity does not expose skill levels yet.");
        if (!allowOverride && task.Skills.Any(skill => !volunteer.SkillIds.Contains(skill.SkillId)))
            throw new DomainRuleException("TASK_SKILL_MISMATCH", "The volunteer does not match all required skills.");
    }
}
