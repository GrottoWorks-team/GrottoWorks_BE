namespace TaskExecution.Domain.Tasks;

public sealed class WorkTask
{
    private WorkTask()
    {
    }

    public Guid Id { get; private set; }
    public Guid WorkAreaId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public TaskType Type { get; private set; }
    public Guid? PurchaseRequestId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? CompletionCriteria { get; private set; }
    public TaskPriority Priority { get; private set; }
    public decimal? EstimatedHours { get; private set; }
    public int RequiredVolunteerCount { get; private set; }
    public DateTimeOffset? StartAt { get; private set; }
    public DateTimeOffset? Deadline { get; private set; }
    public TaskStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static WorkTask CreateDraft(
        Guid id,
        Guid workAreaId,
        Guid createdByUserId,
        string name,
        string? description,
        string? completionCriteria,
        TaskPriority priority,
        int? estimatedMinutes,
        int requiredVolunteerCount,
        DateTimeOffset? startAt,
        DateTimeOffset? deadline,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || workAreaId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw DomainRuleException.Validation("TASK_IDENTITY_INVALID", "Task, work area and creator IDs are required.");
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 160)
        {
            throw DomainRuleException.Validation("TASK_NAME_INVALID", "Task name must contain 1 to 160 characters.");
        }

        if (requiredVolunteerCount < 1)
        {
            throw DomainRuleException.Validation("TASK_VOLUNTEER_COUNT_INVALID", "At least one volunteer is required.");
        }

        if (estimatedMinutes is <= 0 or > 599999)
        {
            throw DomainRuleException.Validation("TASK_ESTIMATE_INVALID", "Estimated minutes must be between 1 and 599999.");
        }

        if (startAt.HasValue && deadline.HasValue && deadline.Value <= startAt.Value)
        {
            throw DomainRuleException.Validation("TASK_DATE_RANGE_INVALID", "Task deadline must be after its start time.");
        }

        return new WorkTask
        {
            Id = id,
            WorkAreaId = workAreaId,
            CreatedByUserId = createdByUserId,
            Type = TaskType.Work,
            Name = normalizedName,
            Description = NormalizeOptional(description),
            CompletionCriteria = NormalizeOptional(completionCriteria),
            Priority = priority,
            EstimatedHours = estimatedMinutes.HasValue ? estimatedMinutes.Value / 60m : null,
            RequiredVolunteerCount = requiredVolunteerCount,
            StartAt = startAt?.ToUniversalTime(),
            Deadline = deadline?.ToUniversalTime(),
            Status = TaskStatus.Draft,
            Version = 1,
            CreatedAt = createdAt.ToUniversalTime()
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
