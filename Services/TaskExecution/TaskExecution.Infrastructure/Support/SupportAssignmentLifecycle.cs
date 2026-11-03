using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Support;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Support;

public sealed class SupportAssignmentLifecycle(TaskExecutionDbContext db, TaskExecution.Application.Events.ITaskEventOutbox outbox, TaskExecution.Application.Events.ITaskCorrelationContext correlation) : ISupportAssignmentLifecycle
{
    public async Task CompleteAssignmentsAsync(IReadOnlyCollection<Guid> assignmentIds,DateTimeOffset now,CancellationToken cancellationToken)
    {
        if(assignmentIds.Count==0) return;
        var requests=await db.SupportRequests.Where(request=>request.Responses.Any(response=>response.Members.Any(member=>member.AssignmentId.HasValue && assignmentIds.Contains(member.AssignmentId.Value))))
            .Include(request=>request.Responses).ThenInclude(response=>response.Members).ToListAsync(cancellationToken);
        foreach(var request in requests)
        {
            var area=await db.WorkAreaScopes.SingleAsync(row=>row.WorkAreaId==request.WorkAreaId,cancellationToken);
            foreach(var member in request.CompleteAssignments(assignmentIds,now))
                outbox.Add(new TaskExecution.Contracts.Events.TaskIntegrationEvent(Guid.NewGuid(),"support.member.completed",1,now,
                    correlation.CorrelationId,request.RequestedBy,area.ParishId,area.SeasonId,"support-request",request.Id,request.Version,
                    System.Text.Json.JsonSerializer.SerializeToElement(new {supportRequestId=request.Id,memberId=member.Id,volunteerUserId=member.VolunteerId,
                        assignmentId=member.AssignmentId,workAreaId=request.WorkAreaId,leaderUserId=area.LeaderUserId,recipientUserIds=new[]{member.VolunteerId}})));
        }
    }
    public async Task RespondAsync(Guid assignmentId,bool accept,string? reason,DateTimeOffset now,CancellationToken cancellationToken)
    {
        var request=await db.SupportRequests.Where(row=>row.Responses.Any(response=>response.Members.Any(member=>member.AssignmentId==assignmentId)))
            .Include(row=>row.Positions).Include(row=>row.Materials)
            .Include(row=>row.Responses).ThenInclude(response=>response.Members)
            .Include(row=>row.Responses).ThenInclude(response=>response.Materials).AsSplitQuery().SingleOrDefaultAsync(cancellationToken)
            ?? throw TaskExecution.Domain.DomainRuleException.NotFound("SUPPORT_MEMBER_NOT_FOUND","The assignment has no support member.");
        request.RespondToNomination(assignmentId,accept,reason,now);
    }
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
