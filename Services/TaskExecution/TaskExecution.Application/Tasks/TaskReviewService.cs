using BuildingBlocks.Security;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Tasks;

public sealed class TaskReviewService(ITaskRepository tasks, IAssignmentStore assignments, ITaskReviewStore store,
    TaskScopeAccess scope, ActiveUserAccess users, ICurrentUser user, TaskEventRecorder events,
    ITaskFileGateway files, ITaskResourceGateway resource, ISupportAssignmentLifecycle support)
{
    public async Task<IReadOnlyList<TaskProgressDto>> ListAsync(Guid taskId, CancellationToken ct)
    {
        var task=await FindAsync(taskId, ct);
        await AuthorizeReadAsync(task, ct);
        return await store.ListAsync(taskId, ct);
    }
    public async Task<TaskReviewPackage> PackageAsync(Guid taskId, CancellationToken ct)
    {
        var task=await FindAsync(taskId, ct);
        await scope.GetWorkAreaForLeaderReadAsync(task.WorkAreaId, ct);
        var material=await resource.MaterialSummaryAsync(taskId, ct);
        return new(TaskDto.From(task), task.CompletionCriteria, await store.ListAsync(taskId, ct),
            await store.ReviewsAsync(taskId, ct), await store.ApprovedHoursAsync(taskId, ct), material);
    }
    public async Task<(IReadOnlyList<TaskProgressDto> Items,long Total)> GetPageAsync(Guid taskId,int page,int size,bool ascending,CancellationToken ct)
    {
        var task=await FindAsync(taskId,ct);
        await AuthorizeReadAsync(task,ct);
        return await store.GetPageAsync(taskId,page,size,ascending,ct);
    }
    public async Task<TaskProgressDto> AddAsync(Guid taskId, AddProgressCommand command, bool submission, bool resubmit, CancellationToken ct)
    {
        var now=DateTimeOffset.UtcNow;
        var progress=TaskProgress.Create(Guid.NewGuid(), taskId, user.UserId, command.PercentComplete, command.Note, submission, now);
        var evidence=(command.Evidence ?? []).Select(row=>ProgressEvidence.Create(progress.Id,row.FileId,row.Caption,now)).ToArray();
        if (evidence.Length > 10 || evidence.Select(row=>row.FileId).Distinct().Count()!=evidence.Length)
            throw DomainRuleException.Validation("TASK_EVIDENCE_INVALID", "Provide up to ten distinct files.");
        // Authorize before creating durable reservation intents or contacting the file service.
        var initial=await FindAsync(taskId,ct);
        await AuthorizeVolunteerAsync(initial,ct);
        var intents=evidence.Select(row=>new AttachmentIntent(Guid.NewGuid(),taskId,progress.Id,row.FileId,user.UserId,now)).ToArray();
        await store.PrepareAttachmentsAsync(intents,ct);
        return await assignments.InTaskTransactionAsync(taskId,async ()=>
        {
            var task=await tasks.GetByIdForUpdateAsync(taskId,ct) ?? throw Missing();
            var area=await AuthorizeVolunteerAsync(task,ct);
            await store.EnsureAttachmentsActiveAsync(intents,ct);
            task.RecordProgress(submission,resubmit,command.Version,now);
            foreach(var intent in intents) await files.ReserveAsync(intent,ct);
            store.Add(progress,evidence);
            if(submission) events.Record("task.progress.submitted",task,area,new {
                taskId,workAreaId=task.WorkAreaId,progressId=progress.Id,volunteerUserId=user.UserId,
                leaderUserId=area.LeaderUserId,recipientUserIds=new[]{area.LeaderUserId}.Where(id=>id.HasValue).Select(id=>id!.Value).ToArray(),resubmission=resubmit });
            await assignments.SaveAsync(ct);
            return new TaskProgressDto(progress.Id,taskId,user.UserId,progress.PercentComplete,progress.Note,now,submission,null,null,evidence);
        },ct);
    }
    public async Task<TaskDto> ReviewAsync(Guid taskId, bool approve, string? reason, int? version, CancellationToken ct)
    {
        return await assignments.InTaskTransactionAsync(taskId,async ()=>
        {
            var task=await tasks.GetByIdForUpdateAsync(taskId,ct) ?? throw Missing();
            var (area,_)=await scope.GetWorkAreaForLeaderWriteAsync(task.WorkAreaId,ct);
            await users.EnsureCallerAsync(area.ParishId,ct);
            var progress=await store.LatestSubmissionAsync(taskId,ct)
                ?? throw new DomainRuleException("TASK_SUBMISSION_REQUIRED","A completion submission is required.");
            var now=DateTimeOffset.UtcNow;
            var review=TaskCompletionReview.Create(taskId,progress.Id,user.UserId,approve,reason,now);
            task.ReviewCompletion(approve,version,now);
            var members=await assignments.ListAsync(taskId,ct);
            var active=members.Where(row=>row.Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress).ToArray();
            if(approve)
            {
                progress.Approve(user.UserId,now);
                foreach(var assignment in active) assignment.CompleteForTask();
                await support.CompleteAssignmentsAsync(active.Select(row=>row.Id).ToArray(),now,ct);
            }
            store.Add(review);
            events.Record("task.completion.reviewed",task,area,new {
                taskId,workAreaId=task.WorkAreaId,progressId=progress.Id,approved=approve,decision=approve ? "APPROVED" : "REVISION_REQUIRED",
                seasonId=area.SeasonId,parishId=area.ParishId,taskName=task.Name,
                taskType=System.Text.Json.JsonNamingPolicy.SnakeCaseUpper.ConvertName(task.Type.ToString()),
                volunteerUserIds=active.Select(row=>row.VolunteerId).Distinct().ToArray(),reviewedByUserId=user.UserId,reviewedAt=now,
                reason=review.Reason,leaderUserId=area.LeaderUserId,recipientUserIds=active.Select(row=>row.VolunteerId).Append(user.UserId).Distinct().ToArray(),
                status=approve ? "COMPLETED" : "REVISION_REQUIRED" });
            await assignments.SaveAsync(ct);
            return TaskDto.From(task);
        },ct);
    }
    public async Task<FileDownload> DownloadAsync(Guid taskId,Guid progressId,Guid fileId,CancellationToken ct)
    {
        var task=await FindAsync(taskId,ct);
        await AuthorizeReadAsync(task,ct);
        var progress=(await store.ListAsync(taskId,ct)).SingleOrDefault(row=>row.Id==progressId);
        if(progress is null || !progress.Evidence.Any(row=>row.FileId==fileId))
            throw DomainRuleException.NotFound("TASK_EVIDENCE_NOT_FOUND","The file does not belong to this progress record.");
        var attachment=await store.FindAttachmentAsync(progressId,fileId,ct)
            ?? throw new DomainRuleException("TASK_EVIDENCE_ATTACHMENT_PENDING","Attachment recovery must complete before download.",DomainErrorKind.ServiceUnavailable);
        await files.FinalizeAsync(attachment,ct);
        return await files.DownloadAsync(fileId,progressId,ct);
    }
    private async Task<TaskExecution.Domain.Scopes.WorkAreaScope> AuthorizeVolunteerAsync(WorkTask task,CancellationToken ct)
    {
        var (area,_)=await scope.GetWorkAreaForVolunteerWriteAsync(task.WorkAreaId,ct);
        await users.EnsureCallerAsync(area.ParishId,ct);
        var rows=await assignments.ListAsync(task.Id,ct);
        var mine=rows.SingleOrDefault(row=>row.VolunteerId==user.UserId && row.Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress);
        if(mine is null)
            throw DomainRuleException.Forbidden("TASK_ASSIGNMENT_REQUIRED","An accepted active assignment is required.");
        // Task đã chỉ định trưởng nhóm → chỉ assignment đó được ghi tiến độ / nộp hoàn thành.
        // Task chưa chỉ định (null) giữ hành vi cũ: mọi TNV đã nhận việc đều ghi được.
        if(task.TeamLeaderAssignmentId is { } leaderAssignmentId && leaderAssignmentId != mine.Id)
            throw DomainRuleException.Forbidden("TASK_TEAM_LEADER_ONLY","Only the designated task team leader can submit progress.");
        return area;
    }
    private async Task AuthorizeReadAsync(WorkTask task,CancellationToken ct)
    {
        var area=await scope.GetWorkAreaForReadAsync(task.WorkAreaId,ct);
        if(user.Role==GrottoWorksRoles.Leader && area.LeaderUserId==user.UserId) return;
        if(user.Role==GrottoWorksRoles.Parish) return;
        if(user.Role==GrottoWorksRoles.Volunteer && (await assignments.ListAsync(task.Id,ct))
            .Any(row=>row.VolunteerId==user.UserId && row.Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress or AssignmentStatus.Completed)) return;
        throw DomainRuleException.Forbidden("TASK_PROGRESS_ACCESS_DENIED","Task membership or assigned leader access is required.");
    }
    private async Task<WorkTask> FindAsync(Guid id,CancellationToken ct)=>await tasks.GetByIdAsync(id,ct) ?? throw Missing();
    private static DomainRuleException Missing()=>DomainRuleException.NotFound("TASK_NOT_FOUND","The task does not exist.");
}
