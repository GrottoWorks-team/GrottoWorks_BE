using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Tasks;

public sealed class MyTasksStore(TaskExecutionDbContext db) : IMyTasksStore
{
    public async Task<(IReadOnlyList<WorkTask> Items, long Total)> GetAsync(Guid volunteerId, Guid parishId,
        MyTasksFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Tasks.AsNoTracking().Where(task =>
            db.Assignments.Any(row => row.TaskId == task.Id && row.VolunteerId == volunteerId &&
                (row.Type == AssignmentType.Direct || row.ApprovedAt.HasValue)) &&
            db.WorkAreaScopes.Any(area => area.WorkAreaId == task.WorkAreaId && area.ParishId == parishId &&
                (!filter.SeasonId.HasValue || area.SeasonId == filter.SeasonId.Value)));
        if (filter.Status.HasValue) query = query.Where(task => task.Status == filter.Status.Value);
        if (filter.TaskType.HasValue) query = query.Where(task => task.Type == filter.TaskType.Value);
        var total = await query.LongCountAsync(cancellationToken);
        var descending = (filter.Sort ?? "createdAt,desc").EndsWith(",desc", StringComparison.OrdinalIgnoreCase);
        var field = filter.Sort?.Split(',')[0] ?? "createdAt";
        var sorted = field switch
        {
            "name" => descending ? query.OrderByDescending(t=>t.Name).ThenBy(t=>t.Id) : query.OrderBy(t=>t.Name).ThenBy(t=>t.Id),
            "deadline" => descending ? query.OrderByDescending(t=>t.Deadline).ThenBy(t=>t.Id) : query.OrderBy(t=>t.Deadline).ThenBy(t=>t.Id),
            _ => descending ? query.OrderByDescending(t=>t.CreatedAt).ThenBy(t=>t.Id) : query.OrderBy(t=>t.CreatedAt).ThenBy(t=>t.Id)
        };
        return (await sorted.Skip((filter.Page-1)*filter.Size).Take(filter.Size).ToListAsync(cancellationToken), total);
    }
}
