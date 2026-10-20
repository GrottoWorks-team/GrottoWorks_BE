namespace TaskExecution.Domain.Tasks;

public enum TaskType
{
    Work,
    Purchase,
    Corrective
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum TaskStatus
{
    Draft,
    Open,
    NotStarted,
    InProgress,
    Submitted,
    RevisionRequired,
    Completed,
    Cancelled
}
