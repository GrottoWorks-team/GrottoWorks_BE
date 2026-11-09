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

public sealed partial class AssignmentService(IAssignmentStore assignments, ITaskRepository tasks,
    TaskScopeAccess scopeAccess, ActiveUserAccess users, ICurrentUser user, TaskEventRecorder events,
    TaskExecution.Application.Support.ISupportAssignmentLifecycle support)
{
    public async Task<AssignmentDto> RespondAsync(Guid assignmentId, bool accept, string? reason, int? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var assignmentInfo = await assignments.GetAsync(assignmentId, cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_ASSIGNMENT_NOT_FOUND", "The assignment does not exist.");
        if (user.Role != GrottoWorksRoles.Volunteer || assignmentInfo.VolunteerId != user.UserId)
            throw DomainRuleException.Forbidden("TASK_ASSIGNMENT_ACCESS_DENIED", "Only the assigned volunteer can respond.");
        return await assignments.InTaskTransactionAsync(assignmentInfo.TaskId, async () =>
        {
            var assignment=await assignments.GetForUpdateAsync(assignmentId,cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_ASSIGNMENT_NOT_FOUND","The assignment does not exist.");
            var task = await tasks.GetByIdForUpdateAsync(assignment.TaskId, cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
            var (area, _) = await scopeAccess.GetWorkAreaForVolunteerWriteAsync(task.WorkAreaId, cancellationToken);
            await users.EnsureCallerAsync(area.ParishId, cancellationToken);
            if (task.Status is TaskExecution.Domain.Tasks.TaskStatus.Cancelled or TaskExecution.Domain.Tasks.TaskStatus.Completed)
                throw new DomainRuleException("TASK_INVALID_STATE", "The task no longer accepts assignment responses.");
            if (accept) assignment.Accept(expectedVersion, DateTimeOffset.UtcNow);
            else assignment.Decline(reason!, expectedVersion, DateTimeOffset.UtcNow);
            if(assignment.Type==AssignmentType.Support)
                await support.RespondAsync(assignment.Id,accept,assignment.ResponseReason,DateTimeOffset.UtcNow,cancellationToken);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            if (!accept)
                task.ClearInvalidTeamLeaderForCurrentChange(assignment.Id);
            events.Record(TaskEventTypes.AssignmentResponded, task, area, new
            {
                assignmentId = assignment.Id, taskId = task.Id, workAreaId = area.WorkAreaId,
                volunteerUserId = user.UserId, leaderUserId = area.LeaderUserId, assignmentType = assignment.Type==AssignmentType.Support ? "SUPPORT" : "DIRECT",
                decision = accept ? "ACCEPTED" : "DECLINED", reason = assignment.ResponseReason
            });
            if(accept && task.Type==TaskType.Purchase)
                events.Record("task.purchase-task.assigned",task,area,new {taskId=task.Id,purchaseRequestId=task.PurchaseRequestId,
                    workAreaId=area.WorkAreaId,buyerUserId=assignment.VolunteerId,assignmentId=assignment.Id,leaderUserId=area.LeaderUserId});
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
            EnsureCapacity(existing, task.RequiredVolunteerCount, task.Type!=TaskType.Purchase && command.OverrideCapacity);
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
                volunteerUserId = volunteer.UserId, leaderUserId = area.LeaderUserId,
                status = "PENDING", assignmentType = "DIRECT"
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
            .Where(row => row.Type is AssignmentType.Direct or AssignmentType.Support || row.Status != AssignmentStatus.Pending)
            .Select(row => AssignmentDto.From(row)).ToList();
    }

    /// <summary>
    /// Phân công + đơn đăng ký của chính người gọi (TNV). App mobile cần `id`/`version`
    /// của assignment để gọi accept/decline — trước đây không có đường đọc nào.
    /// </summary>
    public async Task<IReadOnlyList<AssignmentDto>> MyAssignmentsAsync(CancellationToken cancellationToken = default) =>
        (await assignments.ListForVolunteerAsync(user.UserId, cancellationToken))
        .Select(row => AssignmentDto.From(row)).ToList();

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
        if (allowOverride) return;
        foreach (var requirement in task.Skills)
        {
            if (!VolunteerSkills.Meets(volunteer, requirement.SkillId, requirement.RequiredSkillLevel))
                throw new DomainRuleException("TASK_SKILL_MISMATCH", "The volunteer does not meet all required skills and minimum levels.");
        }
    }
}
