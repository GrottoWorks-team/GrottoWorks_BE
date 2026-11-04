using System.Text.Json;
using BuildingBlocks.Security;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Support;
using TaskState=TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Support;

public sealed class ReviewSupportMemberHandler(ISupportRequestRepository requests,IAssignmentStore assignments,ITaskRepository tasks,
    TaskScopeAccess scopes,ActiveUserAccess users,ITaskEventOutbox outbox,TaskEventRecorder events,ITaskCorrelationContext correlation,TimeProvider clock)
{
    public async Task<SupportMemberDto> HandleAsync(Guid memberId,bool approve,string? reason,int? expectedVersion,CancellationToken ct=default)
    {
        var requestId=await requests.FindRequestIdByMemberAsync(memberId,ct)
            ?? throw DomainRuleException.NotFound("SUPPORT_MEMBER_NOT_FOUND","The member does not exist.");
        return await requests.InRequestTransactionAsync(requestId,async ()=>
        {
            var request=await requests.GetAsync(requestId,true,ct)
                ?? throw DomainRuleException.NotFound("SUPPORT_REQUEST_NOT_FOUND","The support request does not exist.");
            var (area,_)=await scopes.GetWorkAreaForLeaderWriteAsync(request.WorkAreaId,ct);
            var actor=await users.EnsureCallerAsync(area.ParishId,ct);
            var response=request.Responses.Single(row=>row.Members.Any(member=>member.Id==memberId));
            var member=response.Members.Single(row=>row.Id==memberId);
            request.EnsureMemberReviewAllowed(member,approve,reason,expectedVersion);
            var now=clock.GetUtcNow();

            if(!approve)
            {
                request.ReviewMember(memberId,false,null,actor.UserId,reason,expectedVersion,now);
                StageReviewEvent(request,member,actor.UserId,area.ParishId,now);
                await requests.SaveAsync(ct);
                return SupportMemberDto.From(member);
            }

            var position=request.Positions.Single(row=>row.Id==member.PositionId);
            var taskId=position.TaskId
                ?? throw DomainRuleException.Validation("SUPPORT_POSITION_TASK_REQUIRED","The position must reference a task.");
            return await assignments.InTaskTransactionAsync(taskId,async ()=>
            {
                var task=await tasks.GetByIdForUpdateAsync(taskId,ct)
                    ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND","The task does not exist.");
                if(task.WorkAreaId!=area.WorkAreaId)
                    throw DomainRuleException.Validation("SUPPORT_POSITION_TASK_MISMATCH","The task belongs to another work area.");
                if(task.Status is not (TaskState.Open or TaskState.NotStarted or TaskState.InProgress or TaskState.RevisionRequired))
                    throw new DomainRuleException("TASK_INVALID_STATE","The task cannot accept support participants.");
                var volunteer=await users.EnsureActiveAsync(member.VolunteerId,area.ParishId,GrottoWorksRoles.Volunteer,ct);
                if(response.RespondingCommunityId.HasValue && volunteer.CommunityId!=response.RespondingCommunityId)
                    throw DomainRuleException.Forbidden("SUPPORT_COMMUNITY_ACCESS_DENIED","The volunteer no longer belongs to the responding community.");
                if(position.Skills.Any(skill=>!VolunteerSkills.Meets(volunteer,skill.SkillId,skill.RequiredSkillLevel)) ||
                    task.Skills.Any(skill=>!VolunteerSkills.Meets(volunteer,skill.SkillId,skill.RequiredSkillLevel)))
                    throw new DomainRuleException("SUPPORT_SKILL_MISMATCH","All position and task skills must be met without override.");
                var existing=await assignments.ListAsync(taskId,ct);
                AssignmentService.EnsureNoDuplicate(existing,member.VolunteerId);
                AssignmentService.EnsureCapacity(existing,task.RequiredVolunteerCount,false);

                var requiresConfirmation=response.VolunteerId!=member.VolunteerId;
                var assignment=TaskAssignment.FromSupportResponse(taskId,member.VolunteerId,actor.UserId,response.CreatedAt,now,requiresConfirmation);
                request.ReviewMember(memberId,true,assignment.Id,actor.UserId,null,expectedVersion,now,requiresConfirmation);
                assignments.Add(assignment);task.RegisterAssignmentChange(now);
                events.Record(TaskEventTypes.AssignmentCreated,task,area,new
                {
                    assignmentId=assignment.Id,taskId,workAreaId=area.WorkAreaId,volunteerUserId=member.VolunteerId,
                    leaderUserId=area.LeaderUserId,assignmentType="SUPPORT",status=requiresConfirmation ? "PENDING" : "ACCEPTED",supportRequestId=request.Id,supportMemberId=member.Id
                });
                StageReviewEvent(request,member,actor.UserId,area.ParishId,now);
                await requests.SaveAsync(ct);
                return SupportMemberDto.From(member);
            },ct);
        },ct);
    }

    private void StageReviewEvent(SupportRequest request,SupportResponseMember member,Guid actor,Guid parishId,DateTimeOffset now)=>
        outbox.Add(new TaskIntegrationEvent(Guid.NewGuid(),TaskEventTypes.SupportMemberReviewed,1,now,correlation.CorrelationId,actor,parishId,
            request.SeasonId,"support_request",request.Id,request.Version,JsonSerializer.SerializeToElement(new
            {
                supportRequestId=request.Id,supportMemberId=member.Id,workAreaId=request.WorkAreaId,volunteerUserId=member.VolunteerId,
                assignmentId=member.AssignmentId,decision=member.Status==SupportMemberStatus.Approved ? "APPROVED" : "REJECTED",
                previousStatus="PENDING",status=JsonNamingPolicy.SnakeCaseUpper.ConvertName(member.Status.ToString()),reason=member.ReviewReason,
                requiresConfirmation=member.RequiresConfirmation
            })));
}
