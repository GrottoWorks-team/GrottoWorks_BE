namespace TaskExecution.Domain.Tasks;

public sealed partial class WorkTask
{
    public string? CancellationReason { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public void Cancel(string reason, Guid actorId, int? expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion.HasValue && expectedVersion != Version)
            throw new DomainRuleException("TASK_VERSION_MISMATCH", "The task changed.", DomainErrorKind.PreconditionFailed);
        if (Status is TaskStatus.Completed or TaskStatus.Cancelled)
            throw new DomainRuleException("TASK_INVALID_STATE", "A completed or cancelled task cannot be cancelled.");
        var normalized = reason?.Trim();
        if (actorId == Guid.Empty || string.IsNullOrWhiteSpace(normalized) || normalized.Length > 500)
            throw DomainRuleException.Validation("TASK_CANCELLATION_INVALID", "Cancellation requires an actor and reason of 1–500 characters.");
        Status = TaskStatus.Cancelled;
        CancellationReason = normalized;
        CancelledBy = actorId;
        CancelledAt = UpdatedAt = now.ToUniversalTime();
        Version++;
    }
}
