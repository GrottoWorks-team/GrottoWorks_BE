namespace TaskExecution.Domain.Assignments;

public enum AssignmentType { Direct, Application }
public enum AssignmentStatus { Pending, Accepted, Declined, InProgress, Completed, Cancelled }

public sealed class TaskAssignment
{
    private TaskAssignment() { }
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid VolunteerId { get; private set; }
    public AssignmentType Type { get; private set; }
    public AssignmentStatus Status { get; private set; }
    public DateTimeOffset? RequestedAt { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? Note { get; private set; }
    public bool OverrideCapacity { get; private set; }
    public bool OverrideSkillMismatch { get; private set; }
    public string? OverrideReason { get; private set; }
    public Guid? AssignedBy { get; private set; }
    public int Version { get; private set; } = 1;

    public bool ReservesCapacity => Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress or AssignmentStatus.Completed ||
        (Status == AssignmentStatus.Pending && Type == AssignmentType.Direct);

    public static TaskAssignment Assign(Guid taskId, Guid volunteerId, Guid leaderId, string? note,
        bool overrideCapacity, bool overrideSkills, string? overrideReason, DateTimeOffset now)
    {
        if (taskId == Guid.Empty || volunteerId == Guid.Empty || leaderId == Guid.Empty)
            throw DomainRuleException.Validation("ASSIGNMENT_IDENTITY_INVALID", "Task, volunteer and leader are required.");
        var reason = overrideReason?.Trim();
        if ((overrideCapacity || overrideSkills) && (string.IsNullOrWhiteSpace(reason) || reason.Length > 500))
            throw DomainRuleException.Validation("ASSIGNMENT_OVERRIDE_REASON_REQUIRED", "Override requires a reason of 1–500 characters.");
        if (note?.Length > 2000)
            throw DomainRuleException.Validation("ASSIGNMENT_NOTE_INVALID", "Note must not exceed 2000 characters.");
        return new TaskAssignment
        {
            Id = Guid.NewGuid(), TaskId = taskId, VolunteerId = volunteerId,
            Type = AssignmentType.Direct, Status = AssignmentStatus.Pending,
            AssignedAt = now.ToUniversalTime(), AssignedBy = leaderId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            OverrideCapacity = overrideCapacity, OverrideSkillMismatch = overrideSkills,
            OverrideReason = overrideCapacity || overrideSkills ? reason : null
        };
    }
}
