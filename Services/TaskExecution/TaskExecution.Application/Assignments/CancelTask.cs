using System.Text.Json;
using TaskExecution.Application.Tasks;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;

namespace TaskExecution.Application.Assignments;

public sealed partial class AssignmentService
{
    public async Task<TaskDto> CancelTaskAsync(Guid taskId, string reason, int? version, CancellationToken cancellationToken = default)
    {
        return await assignments.InTaskTransactionAsync(taskId, async () =>
        {
            var task = await tasks.GetByIdForUpdateAsync(taskId, cancellationToken)
                ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
            var (area, _) = await scopeAccess.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId, cancellationToken);
            await users.EnsureCallerAsync(area.ParishId, cancellationToken);
            var previousStatus = task.Status;
            task.Cancel(reason,user.UserId,version,DateTimeOffset.UtcNow);
            var rows=await assignments.ListAsync(taskId,cancellationToken);
            var supportIds=rows.Where(row=>row.Type==AssignmentType.Support && row.Status is AssignmentStatus.Pending or AssignmentStatus.Accepted or AssignmentStatus.InProgress)
                .Select(row=>row.Id).ToArray();
            foreach (var row in rows)
                row.CancelForTask(task.CancellationReason!);
            await support.CancelAssignmentsAsync(supportIds,DateTimeOffset.UtcNow,cancellationToken);
            events.Record(TaskEventTypes.StatusChanged,task,area,new
            {
                taskId,workAreaId=area.WorkAreaId,
                previousStatus=JsonNamingPolicy.SnakeCaseUpper.ConvertName(previousStatus.ToString()),
                status="CANCELLED",reason=task.CancellationReason,leaderUserId=area.LeaderUserId,
                recipientUserIds=rows.Select(row=>row.VolunteerId).Append(user.UserId).Distinct().ToArray()
            });
            await assignments.SaveAsync(cancellationToken);
            return TaskDto.From(task);
        },cancellationToken);
    }
}
