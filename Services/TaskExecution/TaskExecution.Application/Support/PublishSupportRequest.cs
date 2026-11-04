using System.Text.Json;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Support;

public sealed record SupportPositionTask(Guid Id, Guid WorkAreaId, TaskState Status);

public sealed class PublishSupportRequestHandler(ISupportRequestRepository requests, TaskScopeAccess scopes,
    ActiveUserAccess users, ITaskEventOutbox outbox, ITaskCorrelationContext correlation, TimeProvider clock)
{
    public Task<SupportRequestDto> HandleAsync(Guid requestId, int? expectedVersion, CancellationToken cancellationToken = default) =>
        requests.InRequestTransactionAsync(requestId, async () =>
        {
            var request = await requests.GetAsync(requestId, true, cancellationToken)
                ?? throw DomainRuleException.NotFound("SUPPORT_REQUEST_NOT_FOUND", "The support request does not exist.");
            var (area, _) = await scopes.GetWorkAreaForLeaderWriteAsync(request.WorkAreaId, cancellationToken);
            var actor = await users.EnsureCallerAsync(area.ParishId, cancellationToken);
            request.EnsurePublicationAllowed(expectedVersion);

            var taskIds = request.Positions.Select(position => position.TaskId!.Value).Distinct().ToArray();
            var tasks = await requests.GetPositionTasksAsync(taskIds, cancellationToken);
            var tasksById = tasks.ToDictionary(task => task.Id);
            foreach (var position in request.Positions)
            {
                if (!tasksById.TryGetValue(position.TaskId!.Value, out var task))
                    throw DomainRuleException.NotFound("TASK_NOT_FOUND", "A support position references a task that does not exist.");
                if (task.WorkAreaId != request.WorkAreaId)
                    throw DomainRuleException.Validation("SUPPORT_POSITION_TASK_MISMATCH", "Position tasks must belong to the requesting work area.");
                if (task.Status is TaskState.Completed or TaskState.Cancelled)
                    throw new DomainRuleException("TASK_INVALID_STATE", "A completed or cancelled task cannot recruit support.");
            }

            var now = clock.GetUtcNow();
            request.Publish(expectedVersion, now);
            var payload = JsonSerializer.SerializeToElement(new
            {
                supportRequestId = request.Id, workAreaId = area.WorkAreaId, leaderUserId = area.LeaderUserId,
                communityId = area.CommunityId,
                supportType = JsonNamingPolicy.SnakeCaseUpper.ConvertName(request.SupportType.ToString()),
                positions = request.Positions.Select(position => new
                {
                    positionId = position.Id, taskId = position.TaskId, requiredVolunteerCount = position.RequiredVolunteerCount,
                    skills = position.Skills.Select(skill => new { skillId = skill.SkillId, requiredSkillLevel = skill.RequiredSkillLevel })
                }),
                materials = request.Materials.Select(material => new { materialId = material.MaterialId, requiredQuantity = material.RequiredQuantity })
            });
            outbox.Add(new TaskIntegrationEvent(Guid.NewGuid(), TaskEventTypes.SupportRequestPublished, 1, now,
                correlation.CorrelationId, actor.UserId, area.ParishId, area.SeasonId, "support_request", request.Id, request.Version, payload));
            await requests.SaveAsync(cancellationToken);
            return SupportRequestDto.From(request);
        }, cancellationToken);
}
