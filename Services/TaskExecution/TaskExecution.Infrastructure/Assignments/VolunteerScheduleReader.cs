using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain.Assignments;
using TaskExecution.Infrastructure.Data;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Infrastructure.Assignments;

public sealed class VolunteerScheduleReader(TaskExecutionDbContext db) : IVolunteerScheduleReader
{
    public async Task<IReadOnlySet<Guid>> GetConflictsAsync(IReadOnlyCollection<Guid> volunteerIds, Guid taskId,
        DateTimeOffset? startAt, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        if (!startAt.HasValue || volunteerIds.Count == 0) return new HashSet<Guid>();
        var ids = await (from assignment in db.Assignments.AsNoTracking()
                         join task in db.Tasks.AsNoTracking() on assignment.TaskId equals task.Id
                         where volunteerIds.Contains(assignment.VolunteerId) && task.Id != taskId &&
                             (assignment.Status == AssignmentStatus.Accepted || assignment.Status == AssignmentStatus.InProgress ||
                              (assignment.Status == AssignmentStatus.Pending && assignment.Type == AssignmentType.Direct)) &&
                             task.Status != TaskState.Cancelled && task.Status != TaskState.Completed &&
                             task.StartAt.HasValue && task.StartAt < deadline && task.Deadline > startAt.Value
                         select assignment.VolunteerId).Distinct().ToArrayAsync(cancellationToken);
        return ids.ToHashSet();
    }
}
