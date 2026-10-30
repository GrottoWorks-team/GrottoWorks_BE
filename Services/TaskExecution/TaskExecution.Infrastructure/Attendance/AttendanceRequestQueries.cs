using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.Infrastructure.Attendance;

public sealed partial class AttendanceStore
{
    public async Task<(IReadOnlyList<AttendanceRequest> Items,long Total)> RequestsPageAsync(Guid areaId,AttendanceRequestStatus status,
        int page,int size,bool descending,CancellationToken ct)
    {
        var query=from request in db.AttendanceRequests.AsNoTracking()
                  join assignment in db.Assignments on request.AssignmentId equals assignment.Id
                  join task in db.Tasks on assignment.TaskId equals task.Id
                  where task.WorkAreaId==areaId && request.Status==status select request;
        var total=await query.LongCountAsync(ct);
        var sorted=descending ? query.OrderByDescending(r=>r.RequestedAt).ThenBy(r=>r.Id) : query.OrderBy(r=>r.RequestedAt).ThenBy(r=>r.Id);
        return (await sorted.Skip((page-1)*size).Take(size).ToListAsync(ct),total);
    }
}
