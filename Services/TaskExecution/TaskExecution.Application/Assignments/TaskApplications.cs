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
