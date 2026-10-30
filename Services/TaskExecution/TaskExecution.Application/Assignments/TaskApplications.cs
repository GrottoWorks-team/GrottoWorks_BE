using BuildingBlocks.Security;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;

namespace TaskExecution.Application.Assignments;

public sealed record TaskApplicationDto(Guid Id, Guid TaskId, Guid VolunteerId, string Status,
    string? Note, int Version, DateTimeOffset? RequestedAt, DateTimeOffset? ReviewedAt, string? Reason)
{
    public static TaskApplicationDto From(TaskAssignment row) => new(row.Id, row.TaskId, row.VolunteerId,
        row.Status switch
        {
            AssignmentStatus.Pending => "PENDING", AssignmentStatus.Declined => "REJECTED",
            AssignmentStatus.Cancelled => "CANCELLED", _ => "APPROVED"
        }, row.Note, row.Version, row.RequestedAt, row.ApprovedAt ?? row.RejectedAt, row.ResponseReason);
}

public sealed partial class AssignmentService
{
    public async Task<AssignmentDto> ApproveApplicationAsync(Guid id, int? version, CancellationToken cancellationToken = default) =>
        AssignmentDto.From(await ReviewApplicationAsync(id, true, null, version, cancellationToken));

    public async Task<TaskApplicationDto> RejectApplicationAsync(Guid id, string reason, int? version, CancellationToken cancellationToken = default) =>
        TaskApplicationDto.From(await ReviewApplicationAsync(id, false, reason, version, cancellationToken));

    private async Task<TaskAssignment> ReviewApplicationAsync(Guid id, bool approve, string? reason, int? version, CancellationToken cancellationToken)
    {
        var row = await assignments.GetAsync(id, cancellationToken);
        if (row is null || row.Type != AssignmentType.Application)
            throw DomainRuleException.NotFound("TASK_APPLICATION_NOT_FOUND", "The application does not exist.");
        return await assignments.InTaskTransactionAsync(row.TaskId, async () =>
        {
            var task = await tasks.GetByIdForUpdateAsync(row.TaskId, cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
            var (area, _) = await scopeAccess.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId, cancellationToken);
            await users.EnsureCallerAsync(area.ParishId, cancellationToken);
            task.EnsurePlanningCanBeUpdated(null);
            row.EnsureVersion(version);
            if (approve)
            {
                var volunteer = await users.EnsureActiveAsync(row.VolunteerId, area.ParishId, GrottoWorksRoles.Volunteer, cancellationToken);
                var existing = await assignments.ListAsync(task.Id, cancellationToken);
                EnsureCapacity(existing, task.RequiredVolunteerCount, false);
                EnsureSkills(task, volunteer, false);
            }
            row.ReviewApplication(approve, user.UserId, reason, version, DateTimeOffset.UtcNow);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            events.Record(TaskEventTypes.AssignmentResponded, task, area, new
            {
                assignmentId = row.Id, taskId = task.Id, workAreaId = area.WorkAreaId,
                volunteerUserId = row.VolunteerId, decision = approve ? "ACCEPTED" : "REJECTED", reason = row.ResponseReason
            });
            await assignments.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);
    }

    public async Task<TaskApplicationDto> ApplyAsync(Guid taskId, string? note, CancellationToken cancellationToken = default)
    {
        return await assignments.InTaskTransactionAsync(taskId, async () =>
        {
            var task = await tasks.GetByIdForUpdateAsync(taskId, cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
            var (area, _) = await scopeAccess.GetWorkAreaForVolunteerWriteAsync(task.WorkAreaId, cancellationToken);
            var volunteer = await users.EnsureCallerAsync(area.ParishId, cancellationToken);
            task.EnsurePlanningCanBeUpdated(null);
            var existing = await assignments.ListAsync(taskId, cancellationToken);
            EnsureNoDuplicate(existing, user.UserId);
            EnsureCapacity(existing, task.RequiredVolunteerCount, false);
            EnsureSkills(task, volunteer, false);
            var application = TaskAssignment.Apply(taskId, user.UserId, note, DateTimeOffset.UtcNow);
            assignments.Add(application);
            task.RegisterAssignmentChange(DateTimeOffset.UtcNow);
            events.Record(TaskEventTypes.AssignmentCreated, task, area, new
            {
                assignmentId = application.Id, taskId, workAreaId = area.WorkAreaId,
                volunteerUserId = user.UserId, status = "PENDING", assignmentType = "APPLICATION"
            });
            await assignments.SaveAsync(cancellationToken);
            return TaskApplicationDto.From(application);
        }, cancellationToken);
    }

    public async Task<(IReadOnlyList<TaskApplicationDto> Items, long Total)> ApplicationsAsync(Guid taskId,
        int page, int size, bool descending, CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdAsync(taskId, cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        await scopeAccess.GetWorkAreaForLeaderReadAsync(task.WorkAreaId, cancellationToken);
        var result = await assignments.ApplicationsPageAsync(taskId, page, size, descending, cancellationToken);
        return (result.Items.Select(TaskApplicationDto.From).ToList(), result.Total);
    }
}
