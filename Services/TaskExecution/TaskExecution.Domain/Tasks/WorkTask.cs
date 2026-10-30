namespace TaskExecution.Domain.Tasks;

public sealed class WorkTask
{
    private readonly List<TaskSkill> _skills = [];

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
    public int EstimatedMinutes { get; private set; }
    public int RequiredVolunteerCount { get; private set; }
    public DateTimeOffset? StartAt { get; private set; }
    public DateTimeOffset Deadline { get; private set; }
    public TaskStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public IReadOnlyCollection<TaskSkill> Skills => _skills;

    public static WorkTask CreateOpen(
        Guid id,
        Guid workAreaId,
        Guid createdByUserId,
        string name,
        string? description,
        string? completionCriteria,
        TaskPriority priority,
        int estimatedMinutes,
        int requiredVolunteerCount,
        DateTimeOffset? startAt,
        DateTimeOffset deadline,
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

        var normalizedDescription = NormalizeOptional(description);
        var normalizedCriteria = NormalizeOptional(completionCriteria);
        ValidateOptionalText(normalizedDescription, 2000, "TASK_DESCRIPTION_INVALID", "Task description");
        ValidateOptionalText(normalizedCriteria, 4000, "TASK_COMPLETION_CRITERIA_INVALID", "Completion criteria");

        if (startAt.HasValue && deadline <= startAt.Value)
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
            Description = normalizedDescription,
            CompletionCriteria = normalizedCriteria,
            Priority = priority,
            EstimatedMinutes = estimatedMinutes,
            RequiredVolunteerCount = requiredVolunteerCount,
            StartAt = startAt?.ToUniversalTime(),
            Deadline = deadline.ToUniversalTime(),
            Status = TaskStatus.Open,
            Version = 1,
            CreatedAt = createdAt.ToUniversalTime()
        };
    }

    public void UpdatePlanning(
        string? name,
        string? description,
        string? completionCriteria,
        TaskPriority? priority,
        int? estimatedMinutes,
        int? requiredVolunteerCount,
        DateTimeOffset? startAt,
        DateTimeOffset? deadline,
        int? expectedVersion,
        DateTimeOffset updatedAt)
    {
        EnsurePlanningCanBeUpdated(expectedVersion);

        var nextName = name is null ? Name : name.Trim();
        var nextVolunteers = requiredVolunteerCount ?? RequiredVolunteerCount;
        var nextDescription = description is null ? Description : NormalizeOptional(description);
        var nextCriteria = completionCriteria is null
            ? CompletionCriteria
            : NormalizeOptional(completionCriteria);
        var nextEstimate = estimatedMinutes ?? EstimatedMinutes;
        var nextStart = startAt?.ToUniversalTime() ?? StartAt;
        var nextDeadline = deadline?.ToUniversalTime() ?? Deadline;

        if (nextName.Length is < 1 or > 160)
        {
            throw DomainRuleException.Validation("TASK_NAME_INVALID", "Task name must contain 1 to 160 characters.");
        }

        if (nextVolunteers < 1)
        {
            throw DomainRuleException.Validation("TASK_VOLUNTEER_COUNT_INVALID", "At least one volunteer is required.");
        }

        if (estimatedMinutes is <= 0 or > 599999)
        {
            throw DomainRuleException.Validation("TASK_ESTIMATE_INVALID", "Estimated minutes must be between 1 and 599999.");
        }

        ValidateOptionalText(nextDescription, 2000, "TASK_DESCRIPTION_INVALID", "Task description");
        ValidateOptionalText(nextCriteria, 4000, "TASK_COMPLETION_CRITERIA_INVALID", "Completion criteria");

        if (nextStart.HasValue && nextDeadline <= nextStart.Value)
        {
            throw DomainRuleException.Validation("TASK_DATE_RANGE_INVALID", "Task deadline must be after its start time.");
        }

        Name = nextName;
        Description = nextDescription;
        CompletionCriteria = nextCriteria;
        Priority = priority ?? Priority;
        EstimatedMinutes = nextEstimate;
        RequiredVolunteerCount = nextVolunteers;
        StartAt = nextStart;
        Deadline = nextDeadline;
        UpdatedAt = updatedAt.ToUniversalTime();
        Version++;
    }

    public void EnsurePlanningCanBeUpdated(int? expectedVersion)
    {
        if (Status is not TaskStatus.Open)
        {
            throw new DomainRuleException("TASK_INVALID_STATE", "Only OPEN tasks can be edited.");
        }

        if (expectedVersion.HasValue && expectedVersion.Value != Version)
        {
            throw new DomainRuleException(
                "TASK_VERSION_MISMATCH",
                "The task version does not match the current version.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    public bool ReplaceCompletionCriteria(
        string? completionCriteria,
        int? expectedVersion,
        DateTimeOffset updatedAt)
    {
        EnsurePlanningCanBeUpdated(expectedVersion);

        var normalizedCriteria = NormalizeOptional(completionCriteria);
        if (normalizedCriteria?.Length > 4000)
        {
            throw DomainRuleException.Validation(
                "TASK_COMPLETION_CRITERIA_INVALID",
                "Completion criteria must not exceed 4000 characters.");
        }

        if (CompletionCriteria == normalizedCriteria)
        {
            return false;
        }

        CompletionCriteria = normalizedCriteria;
        UpdatedAt = updatedAt.ToUniversalTime();
        Version++;
        return true;
    }

    public bool ReplaceSkills(
        IReadOnlyCollection<TaskSkillRequirement> requirements,
        int? expectedVersion,
        DateTimeOffset updatedAt)
    {
        EnsurePlanningCanBeUpdated(expectedVersion);

        var normalizedRequirements = NormalizeSkillRequirements(requirements);
        if (HasSameSkillRequirements(normalizedRequirements))
        {
            return false;
        }

        _skills.Clear();
        foreach (var requirement in normalizedRequirements)
        {
            _skills.Add(TaskSkill.Create(Id, requirement));
        }

        UpdatedAt = updatedAt.ToUniversalTime();
        Version++;
        return true;
    }

    public void MarkReady(int? expectedVersion, DateTimeOffset updatedAt)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != Version)
        {
            throw new DomainRuleException(
                "TASK_VERSION_MISMATCH",
                "The task version does not match the current version.",
                DomainErrorKind.PreconditionFailed);
        }

        if (Status is not TaskStatus.Open)
        {
            throw new DomainRuleException(
                "TASK_INVALID_STATE",
                "Only an OPEN task can be marked ready.");
        }

        Status = TaskStatus.NotStarted;
        UpdatedAt = updatedAt.ToUniversalTime();
        Version++;
    }

    public void RegisterAssignmentChange(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now.ToUniversalTime();
    }

    private static IReadOnlyList<TaskSkillRequirement> NormalizeSkillRequirements(
        IReadOnlyCollection<TaskSkillRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var skillIds = new HashSet<Guid>();
        var normalized = new List<TaskSkillRequirement>(requirements.Count);

        foreach (var requirement in requirements)
        {
            if (requirement.SkillId == Guid.Empty)
            {
                throw DomainRuleException.Validation(
                    "TASK_SKILL_ID_INVALID",
                    "Skill ID must not be empty.");
            }

            if (!skillIds.Add(requirement.SkillId))
            {
                throw DomainRuleException.Validation(
                    "TASK_SKILL_DUPLICATE",
                    "A skill can only appear once in a task requirement.");
            }

            var level = NormalizeOptional(requirement.RequiredSkillLevel);
            if (level?.Length > 30)
            {
                throw DomainRuleException.Validation(
                    "TASK_SKILL_LEVEL_INVALID",
                    "Required skill level must not exceed 30 characters.");
            }

            if (requirement.RequiredVolunteerCount is <= 0)
            {
                throw DomainRuleException.Validation(
                    "TASK_SKILL_VOLUNTEER_COUNT_INVALID",
                    "Required volunteer count must be greater than zero when provided.");
            }

            normalized.Add(new TaskSkillRequirement(
                requirement.SkillId,
                level,
                requirement.RequiredVolunteerCount));
        }

        return normalized.OrderBy(requirement => requirement.SkillId).ToList();
    }

    private bool HasSameSkillRequirements(IReadOnlyList<TaskSkillRequirement> requirements)
    {
        if (_skills.Count != requirements.Count)
        {
            return false;
        }

        var current = _skills.OrderBy(skill => skill.SkillId).ToList();
        for (var index = 0; index < current.Count; index++)
        {
            if (current[index].SkillId != requirements[index].SkillId ||
                current[index].RequiredSkillLevel != requirements[index].RequiredSkillLevel ||
                current[index].RequiredVolunteerCount != requirements[index].RequiredVolunteerCount)
            {
                return false;
            }
        }

        return true;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateOptionalText(
        string? value,
        int maximumLength,
        string errorCode,
        string fieldName)
    {
        if (value?.Length > maximumLength)
        {
            throw DomainRuleException.Validation(
                errorCode,
                $"{fieldName} must not exceed {maximumLength} characters.");
        }
    }
}
