using BuildingBlocks.Security;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Tasks;

public sealed record CorrectiveTaskCommand(Guid WorkAreaId, Guid ReadinessItemId, Guid RequestedByUserId, string Name,
    string? Description, string? CompletionCriteria, TaskPriority Priority, int EstimatedMinutes,
    int RequiredVolunteers, DateTimeOffset? StartAt, DateTimeOffset Deadline);
public interface ISpecialTaskStore
{
    Task<T> InTransactionAsync<T>(string key,Func<Task<T>> action,CancellationToken ct);
    Task<WorkTask?> FindPurchaseAsync(Guid requestId,CancellationToken ct);
    Task<TaskDto?> ReplayCorrectiveAsync(string key,Guid itemId,string fingerprint,CancellationToken ct);
    void SaveCorrective(string key,Guid itemId,string fingerprint,TaskDto task);
}
public sealed class SpecialTaskService(ITaskRepository tasks,ISpecialTaskStore store,ITaskResourceGateway resource,
    TaskScopeAccess scope,IScopeProjectionStore scopes,ActiveUserAccess users,ICurrentUser user,TaskEventRecorder events)
{
    public async Task<TaskDto> PurchaseAsync(Guid requestId,CreateTaskCommand command,CancellationToken ct)
    {
        return await store.InTransactionAsync($"purchase-task:{requestId:D}",async ()=>
        {
            var snapshot=await resource.PurchaseRequestAsync(requestId,ct);
            if(snapshot.Id!=requestId || snapshot.Status!="APPROVED")
                throw new DomainRuleException("PURCHASE_REQUEST_NOT_APPROVED","Only an approved purchase request can create a task.");
            if(command.WorkAreaId!=Guid.Empty && snapshot.WorkAreaId!=command.WorkAreaId) throw DomainRuleException.Validation("PURCHASE_REQUEST_SCOPE_MISMATCH","The purchase request belongs to another work area.");
            var (area,season)=await scope.GetWorkAreaForLeaderWriteAsync(snapshot.WorkAreaId,ct);
            await users.EnsureCallerAsync(area.ParishId,ct);
            if(await store.FindPurchaseAsync(requestId,ct) is not null)
                throw new DomainRuleException("PURCHASE_TASK_EXISTS","A purchase task already exists for this request.");
            if(command.RequiredVolunteers!=1) throw DomainRuleException.Validation("PURCHASE_TASK_BUYER_COUNT_INVALID","Purchase tasks require exactly one buyer.");
            TaskScheduleRules.EnsureFitsScope(command.StartAt,command.Deadline,area,season);
            var task=Create(command with { WorkAreaId=snapshot.WorkAreaId },user.UserId); task.SetSpecialType(TaskType.Purchase,requestId);
            tasks.Add(task);events.Record("task.created",task,area,new {taskId=task.Id,workAreaId=area.WorkAreaId,leaderUserId=area.LeaderUserId,taskType="PURCHASE",purchaseRequestId=requestId});
            await tasks.SaveChangesAsync(ct);return TaskDto.From(task);
        },ct);
    }
    public async Task<TaskDto> CorrectiveAsync(string key,CorrectiveTaskCommand command,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(key) || key.Length>200 || command.ReadinessItemId==Guid.Empty)
            throw DomainRuleException.Validation("IDEMPOTENCY_KEY_REQUIRED","A key up to 200 characters and readiness item are required.");
        var fingerprint=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(command)));
        return await store.InTransactionAsync($"corrective-item:{command.ReadinessItemId:D}",async ()=>
        {
            // The global key lock protects reuse of a key for a different readiness item too.
            return await store.InTransactionAsync($"corrective-key:{key}",async ()=>
            {
                var replay=await store.ReplayCorrectiveAsync(key,command.ReadinessItemId,fingerprint,ct);
                if(replay is not null) return replay;
                var area=await scopes.GetWorkAreaAsync(command.WorkAreaId,ct) ?? throw DomainRuleException.NotFound("WORK_AREA_NOT_FOUND","The work area projection is missing.");
                var season=await scopes.GetSeasonAsync(area.SeasonId,ct) ?? throw DomainRuleException.NotFound("SEASON_NOT_FOUND","The season projection is missing.");
                if(season.IsReadOnly) throw new DomainRuleException("SEASON_READ_ONLY","Archived seasons are read-only.");
                if(command.RequestedByUserId!=area.LeaderUserId) throw DomainRuleException.Forbidden("WORK_AREA_ACCESS_DENIED","The requesting user must be the assigned leader.");
                await users.EnsureActiveAsync(command.RequestedByUserId,area.ParishId,GrottoWorksRoles.Leader,ct);
                TaskScheduleRules.EnsureFitsScope(command.StartAt,command.Deadline,area,season);
                var task=Create(new(command.WorkAreaId,command.Name,command.Description,command.CompletionCriteria,command.Priority,command.EstimatedMinutes,
                    command.RequiredVolunteers,command.StartAt,command.Deadline),command.RequestedByUserId);
                task.SetSpecialType(TaskType.Corrective);tasks.Add(task);
                store.SaveCorrective(key,command.ReadinessItemId,fingerprint,TaskDto.From(task));
                events.Record("task.created",task,area,new {taskId=task.Id,workAreaId=area.WorkAreaId,leaderUserId=area.LeaderUserId,readinessItemId=command.ReadinessItemId,taskType="CORRECTIVE"});
                await tasks.SaveChangesAsync(ct);return TaskDto.From(task);
            },ct);
        },ct);
    }
    private static WorkTask Create(CreateTaskCommand c,Guid actor)=>WorkTask.CreateOpen(Guid.NewGuid(),c.WorkAreaId,actor,c.Name,c.Description,c.CompletionCriteria,
        c.Priority,c.EstimatedMinutes,c.RequiredVolunteers,c.StartAt,c.Deadline,DateTimeOffset.UtcNow);
}
