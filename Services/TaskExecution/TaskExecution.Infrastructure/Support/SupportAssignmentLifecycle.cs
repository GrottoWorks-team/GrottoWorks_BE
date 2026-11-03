using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Support;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Support;

public sealed class SupportAssignmentLifecycle(TaskExecutionDbContext db) : ISupportAssignmentLifecycle
{
    public async Task CancelAssignmentsAsync(IReadOnlyCollection<Guid> assignmentIds,DateTimeOffset now,CancellationToken cancellationToken)
    {
        if(assignmentIds.Count==0) return;
        var requests=await db.SupportRequests
            .Where(request=>request.Responses.Any(response=>response.Members.Any(member=>member.AssignmentId.HasValue &&
                assignmentIds.Contains(member.AssignmentId.Value) && member.Status==SupportMemberStatus.Approved)))
            .Include(request=>request.Positions).Include(request=>request.Materials)
            .Include(request=>request.Responses).ThenInclude(response=>response.Members)
            .Include(request=>request.Responses).ThenInclude(response=>response.Materials).AsSplitQuery().ToListAsync(cancellationToken);
        foreach(var request in requests) request.CancelAssignments(assignmentIds,now);
        // The Task's transaction commits these aggregate versions and the cancellation outbox together.
    }
}
