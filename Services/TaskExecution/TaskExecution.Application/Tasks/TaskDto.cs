using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Tasks;

/// <summary>Yêu cầu kỹ năng của task (`PUT /tasks/{id}/skills`).</summary>
public sealed record TaskSkillDto(Guid SkillId, string? RequiredSkillLevel, int? RequiredVolunteerCount);

/// <summary>Nhu cầu vật tư của task (`PUT /tasks/{id}/materials`).</summary>
public sealed record TaskMaterialDto(Guid MaterialId, decimal RequiredQuantity, string? Note);

public sealed record TaskDto(
    Guid Id,
    Guid WorkAreaId,
    string Name,
    string? Description,
    string? CompletionCriteria,
    TaskType TaskType,
    Guid? PurchaseRequestId,
    TaskPriority Priority,
    int EstimatedMinutes,
    int RequiredVolunteers,
    DateTimeOffset? StartAt,
    DateTimeOffset Deadline,
    TaskState Status,
    Guid? TeamLeaderAssignmentId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<TaskSkillDto> Skills,
    IReadOnlyList<TaskMaterialDto> Materials)
{
    public static TaskDto From(WorkTask task) =>
        new(
            task.Id,
            task.WorkAreaId,
            task.Name,
            task.Description,
            task.CompletionCriteria,
            task.Type,
            task.PurchaseRequestId,
            task.Priority,
            task.EstimatedMinutes,
            task.RequiredVolunteerCount,
            task.StartAt,
            task.Deadline,
            task.Status,
            task.TeamLeaderAssignmentId,
            task.Version,
            task.CreatedAt,
            task.UpdatedAt,
            task.Skills
                .OrderBy(skill => skill.SkillId)
                .Select(skill => new TaskSkillDto(
                    skill.SkillId,
                    skill.RequiredSkillLevel,
                    skill.RequiredVolunteerCount))
                .ToArray(),
            task.Materials
                .OrderBy(material => material.MaterialId)
                .Select(material => new TaskMaterialDto(
                    material.MaterialId,
                    material.RequiredQuantity,
                    material.Note))
                .ToArray());
}
