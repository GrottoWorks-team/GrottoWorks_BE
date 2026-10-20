namespace TaskExecution.Domain.Tasks;

public sealed class TaskSkill
{
    private TaskSkill()
    {
    }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid SkillId { get; private set; }
    public string? RequiredSkillLevel { get; private set; }
    public int? RequiredVolunteerCount { get; private set; }

    internal static TaskSkill Create(
        Guid taskId,
        TaskSkillRequirement requirement)
    {
        return new TaskSkill
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            SkillId = requirement.SkillId,
            RequiredSkillLevel = requirement.RequiredSkillLevel,
            RequiredVolunteerCount = requirement.RequiredVolunteerCount
        };
    }
}

public sealed record TaskSkillRequirement(
    Guid SkillId,
    string? RequiredSkillLevel,
    int? RequiredVolunteerCount);
