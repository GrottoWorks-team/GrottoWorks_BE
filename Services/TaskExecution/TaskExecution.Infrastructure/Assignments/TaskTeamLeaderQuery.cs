using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Assignments;
using TaskExecution.Infrastructure.Data;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Infrastructure.Assignments;

public sealed class TaskTeamLeaderQuery(TaskExecutionDbContext db) : ITaskTeamLeaderQuery
{
    public async Task<IReadOnlyList<Guid>> FindActiveTaskIdsAsync(Guid volunteerId, CancellationToken cancellationToken) =>
        await (from assignment in db.Assignments
               join task in db.Tasks on assignment.TaskId equals task.Id
               where assignment.VolunteerId == volunteerId && task.TeamLeaderAssignmentId == assignment.Id &&
                     task.Status != TaskState.Completed && task.Status != TaskState.Cancelled
               select task.Id).ToListAsync(cancellationToken);
}
