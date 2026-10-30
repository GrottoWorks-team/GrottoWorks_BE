using System.Text.Json;
using TaskExecution.Application.Tasks;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;

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
            foreach (var row in await assignments.ListAsync(taskId,cancellationToken))
                row.CancelForTask(task.CancellationReason!);
            events.Record(TaskEventTypes.StatusChanged,task,area,new
            {
                taskId,workAreaId=area.WorkAreaId,
                previousStatus=JsonNamingPolicy.SnakeCaseUpper.ConvertName(previousStatus.ToString()),
                status="CANCELLED",reason=task.CancellationReason
            });
            await assignments.SaveAsync(cancellationToken);
            return TaskDto.From(task);
        },cancellationToken);
    }
}
